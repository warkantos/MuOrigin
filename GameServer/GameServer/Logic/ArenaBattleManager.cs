﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text;
//using System.Windows.Resources;
using System.Windows;
using System.Threading;
using GameServer.Interface;
using Server.Data;
using GameServer.Server;
using Server.TCP;
using Server.Protocol;
using Server.Tools;
using System.Net;
using System.Net.Sockets;
using HSGameEngine.Tools.AStar;

namespace GameServer.Logic
{
    /// <summary>
    /// 角斗赛-武林争霸-调度和管理类===>现在又改名血战地府 -- MU PK之王 liaowei
    /// 战斗到剩下最后一个人
    /// </summary>
    public class ArenaBattleManager
    {
        #region 配置变量定义

        /// <summary>
        /// 最高积分的分值
        /// </summary>
        private int TopPoint = -1;

        /// <summary>
        /// 最高积分的玩家的名称
        /// </summary>
        private string TopRoleName = "";

        /// <summary>
        /// 玩家领取奖励状态 key=roleid  value=奖励领取标记 -- 1.PK场景结束时给奖励 2.玩家中途退出(掉线、主动退出等)给奖励  只给一次
        /// </summary>
        private Dictionary<int, int> GetawardFlag = new Dictionary<int, int>();

        /// <summary>
        /// 血战地府开始调度的时间点
        /// </summary>
        private List<string> TimePointsList = new List<string>();

        /// <summary>
        /// 血战地府的场景编号
        /// </summary>
        private int MapCode = -1;

        /// <summary>
        /// 要求的最低转生级别
        /// </summary>
        private int MinChangeLifeLev = 0;

        /// <summary>
        /// 要求的最低级别
        /// </summary>
        private int MinLevel = 20;

        /// <summary>
        /// 全区要求的最少在线人数
        /// </summary>
        private int MinRequestNum = 100;

        /// <summary>
        /// 允许进入血战地府场景的最多人数
        /// </summary>
        private int MaxEnterNum = 300;

        /// <summary>
        /// 每次血战地府活动结束后，分配的奖品的数量
        /// </summary>
        private int FallGiftNum = 5;

        /// <summary>
        /// 奖励的物品的掉落ID
        /// </summary>
        private int FallID = -1;

        /// <summary>
        /// 禁止使用的物品ID字符串
        /// </summary>
        private string DisableGoodsIDs = "";

        /// <summary>
        /// 固定给予的奖励物品的ID列表
        /// </summary>
        private List<GoodsData> GiveAwardsGoodsDataList = null;

        /// <summary>
        /// 多长时间加一次经验给玩家
        /// </summary>
        private int AddExpSecs = 60;

        /// <summary>
        /// 多长时间通知阵营积分信息给玩家
        /// </summary>
        private int NotifyBattleKilledNumSecs = 30;

        /// <summary>
        /// 等待入场的时间
        /// </summary>
        private int WaitingEnterSecs = 30;

        /// <summary>
        /// 准备战斗的时间
        /// </summary>
        private int PrepareSecs = 30;

        /// <summary>
        /// 战斗的时间
        /// </summary>
        private int FightingSecs = 300;

        /// <summary>
        /// 清空玩家的时间
        /// </summary>
        private int ClearRolesSecs = 30;

        /// <summary>
        /// 举行的线路
        /// </summary>
        private int BattleLineID = 1;

        /// <summary>
        /// 推送日期ID       MU新增 [04/24/2013 LiaoWei]
        /// /// </summary>
        public static int m_nPushMsgDayID = -1;

        #endregion 配置变量定义

        #region 运行状态变量

        /// <summary>
        /// 是否战斗中
        /// </summary>
        private BattleStates BattlingState = BattleStates.NoBattle;

        /// <summary>
        /// 不同状态的开始时间
        /// </summary>
        private long StateStartTicks = 0;

        /// <summary>
        /// 外部获取战斗状态
        /// </summary>
        /// <returns></returns>
        public int GetBattlingState()
        {
            return (int)BattlingState;
        }

        /// <summary>
        /// 外部获取剩余时间
        /// </summary>
        /// <returns></returns>
        public int GetBattlingLeftSecs()
        {
            long ticks = DateTime.Now.Ticks / 10000;
            int paramSecs = 0;
            if (BattlingState == BattleStates.PublishMsg) //广播血战地府的消息给在线用户
            {
                paramSecs = WaitingEnterSecs;
            }
            else if (BattlingState == BattleStates.WaitingFight) //等待战斗倒计时(此时禁止新用户进入, 此时伤害无效)
            {
                paramSecs = PrepareSecs;
            }
            else if (BattlingState == BattleStates.StartFight) //开始战斗(倒计时中)
            {
                paramSecs = FightingSecs;
            }
            else if (BattlingState == BattleStates.EndFight) //结束战斗(此时伤害无效)
            {
                paramSecs = ClearRolesSecs;
            }
            else if (BattlingState == BattleStates.ClearBattle) //清空战斗场景
            {
                paramSecs = ClearRolesSecs;
            }

            return (int)(((paramSecs * 1000) - (ticks - StateStartTicks)) / 1000);
        }

        /// <summary>
        /// 上次广播阵营战斗积分的时间
        /// </summary>
        private long LastNotifyBattleKilledNumTicks = 0;

        private HashSet<int> DeadRoleSets = new HashSet<int>();

        #endregion 运行状态变量

        #region 初始化

        /// <summary>
        /// 加载参数(4字节赋值，不考虑线程安全)
        /// </summary>
        public void LoadParams()
        {
            SystemXmlItem systemBattle = null;
            if (!GameManager.SystemArenaBattle.SystemXmlItemDict.TryGetValue(1, out systemBattle))
            {
                return;
            }

            List<string> timePointsList = new List<string>();
            string[] fields = null;
            string timePoints = systemBattle.GetStringValue("TimePoints");
            if (null != timePoints && timePoints != "")
            {
                fields = timePoints.Split(',');
                for (int i = 0; i < fields.Length; i++)
                {
                    timePointsList.Add(fields[i].Trim());
                }
            }

            TimePointsList = timePointsList;

            MapCode = systemBattle.GetIntValue("MapCode");
            MinChangeLifeLev = systemBattle.GetIntValue("MinZhuanSheng");
            MinLevel = systemBattle.GetIntValue("MinLevel");
            MinRequestNum = systemBattle.GetIntValue("MinRequestNum");
            MaxEnterNum = systemBattle.GetIntValue("MaxEnterNum");
            FallGiftNum = systemBattle.GetIntValue("FallGiftNum");
            FallID = systemBattle.GetIntValue("FallID");
            DisableGoodsIDs = systemBattle.GetStringValue("DisableGoodsIDs");
            AddExpSecs = systemBattle.GetIntValue("AddExpSecs");


            //20秒到 100秒之间
            NotifyBattleKilledNumSecs = Global.GMax(20, Global.GMin(100, systemBattle.GetIntValue("NotifyBattleKilledNumSecs")));

            WaitingEnterSecs = systemBattle.GetIntValue("WaitingEnterSecs"); ;
            PrepareSecs = systemBattle.GetIntValue("PrepareSecs"); ;
            FightingSecs = systemBattle.GetIntValue("FightingSecs"); ;
            ClearRolesSecs = systemBattle.GetIntValue("ClearRolesSecs");
            BattleLineID = Global.GMax(1, systemBattle.GetIntValue("LineID"));

            ReloadGiveAwardsGoodsDataList(systemBattle);

            m_nPushMsgDayID = Global.SafeConvertToInt32(GameManager.GameConfigMgr.GetGameConifgItem(GameConfigNames.PKKingPushMsgDayID));
        }

        public void ReloadGiveAwardsGoodsDataList(SystemXmlItem systemBattle = null)
        {
            if (null == systemBattle)
            {
                if (!GameManager.SystemArenaBattle.SystemXmlItemDict.TryGetValue(1, out systemBattle))
                {
                    return;
                }
            }

            List<GoodsData> goodsDataList = new List<GoodsData>();
            string giveGoodsIDs = systemBattle.GetStringValue("GiveGoodsIDs").Trim();
            string[] fields = giveGoodsIDs.Split(',');
            if (null != fields && fields.Length > 0)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    if (string.IsNullOrEmpty(fields[i].Trim()))
                    {
                        continue;
                    }

                    int goodsID = Convert.ToInt32(fields[i].Trim());
                    SystemXmlItem systemGoods = null;
                    if (!GameManager.SystemGoods.SystemXmlItemDict.TryGetValue(goodsID, out systemGoods))
                    {
                        LogManager.WriteLog(LogTypes.Error, string.Format("PK之王配置文件中，配置的固定物品奖励中的物品不存在, GoodsID={0}", goodsID));
                        continue;
                    }

                    GoodsData goodsData = new GoodsData()
                    {
                        Id = -1,
                        GoodsID = goodsID,
                        Using = 0,
                        Forge_level = 0,
                        Starttime = "1900-01-01 12:00:00",
                        Endtime = Global.ConstGoodsEndTime,
                        Site = 0,
                        Quality = (int)GoodsQuality.White,
                        Props = "",
                        GCount = 1,
                        Binding = 0,
                        Jewellist = "",
                        BagIndex = 0,
                        AddPropIndex = 0,
                        BornIndex = 0,
                        Lucky = 0,
                        Strong = 0,
                        ExcellenceInfo = 0,
                        AppendPropLev = 0,
                        ChangeLifeLevForEquip = 0,
                    };

                    goodsDataList.Add(goodsData);
                }
            }

            GiveAwardsGoodsDataList = goodsDataList;
        }

        /// <summary>
        /// 初始化血战地府
        /// </summary>
        public void Init()
        {
            //加载参数
            LoadParams();

            ////////////////////////////////////////////////////////////////

            //是否允许攻击
            AllowAttack = false;

            //战斗开始前的人数
            StartRoleNum = 0;

            //当前总的在线用户数
            TotalClientCount = 0;

            //已经杀死的角色的人数
            _AllKilledRoleNum = 0;

            // 向DB请求最高积分信息
            Global.QueryDayActivityTotalPointInfoToDB(SpecialActivityTypes.TheKingOfPK);
        }

        #endregion 初始化

        #region 处理方法

        /// <summary>
        /// 调度和管理血战地府场景
        /// </summary>
        public void Process()
        {
            //是否战斗中
            if (BattlingState > BattleStates.NoBattle)
            {
                //处理正在战斗的过程
                ProcessBattling();
            }
            else
            {
                //处理非战斗期间的逻辑
                ProcessNoBattle();
            }
        }

        /// <summary>
        /// 处理正在战斗的过程
        /// </summary>
        private void ProcessBattling()
        {
            if (BattlingState == BattleStates.PublishMsg) //广播血战地府的消息给在线用户
            {
                // 消息推送
                int nNow = DateTime.Now.DayOfYear;

                if (m_nPushMsgDayID != nNow)
                {
                    //Global.DayActivityTiggerPushMessage((int)SpecialActivityTypes.TheKingOfPK);

                    Global.UpdateDBGameConfigg(GameConfigNames.PKKingPushMsgDayID, nNow.ToString());

                    m_nPushMsgDayID = nNow;
                }

                //判断如果超过了最大等待时间， 先禁止进入血战地府
                long ticks = DateTime.Now.Ticks / 10000;
                if (ticks >= (StateStartTicks + (WaitingEnterSecs * 1000)))
                {
                    //向血战地府场景内的角色发送准备战斗倒计时消息
                    //发送广播消息
                    GameManager.ClientMgr.NotifyArenaBattleInviteMsg(Global._TCPManager.MySocketListener, Global._TCPManager.TcpOutPacketPool, MapCode, (int)BattleCmds.Time, (int)BattleStates.WaitingFight, PrepareSecs);

                    BattlingState = BattleStates.WaitingFight;
                    StateStartTicks = DateTime.Now.Ticks / 10000;
                }
            }
            else if (BattlingState == BattleStates.WaitingFight) //等待战斗倒计时(此时禁止新用户进入, 此时伤害无效)
            {
                //如果超过了最大倒计时时间，允许角色进入战斗伤害
                long ticks = DateTime.Now.Ticks / 10000;
                if (ticks >= (StateStartTicks + (PrepareSecs * 1000)))
                {
                    //是否允许攻击
                    AllowAttack = true;

                    //向血战地府场景内的角色发送战斗倒计时消息
                    //发送广播消息
                    GameManager.ClientMgr.NotifyArenaBattleInviteMsg(Global._TCPManager.MySocketListener, Global._TCPManager.TcpOutPacketPool, MapCode, (int)BattleCmds.Time, (int)BattleStates.StartFight, FightingSecs);

                    //开始时角色的人数
                    StartRoleNum = GameManager.ClientMgr.GetMapClientsCount(MapCode);

                    EnterBattleClientCount = StartRoleNum;
                    AllKilledRoleNum = 0;

                    BattlingState = BattleStates.StartFight;
                    StateStartTicks = DateTime.Now.Ticks / 10000;

                    //上次阵营积分通知的时间
                    LastNotifyBattleKilledNumTicks = StateStartTicks;

                    ProcessTimeNotifyBattleKilledNum(true);
                }
            }
            else if (BattlingState == BattleStates.StartFight) //开始战斗(倒计时中)
            {
                //如果超过了最大倒计时时间，允许角色进入战斗伤害
                long ticks = DateTime.Now.Ticks / 10000;

                //战斗时间结束，停止战斗
                if (ticks >= (StateStartTicks + (FightingSecs * 1000)))
                {
                    //是否允许攻击
                    AllowAttack = false;

                    BattlingState = BattleStates.EndFight;
                    StateStartTicks = DateTime.Now.Ticks / 10000;
                }
                //如果超过了30秒，且血战地府中只剩余一个人,同样结束战斗
                else if (ticks >= (StateStartTicks + (30 * 1 * 1000)))
                {
                    //现在角色的人数
                    // int roleNum = GameManager.ClientMgr.GetMapClientsCount(MapCode);                    
                    // 不能根据地图中现有的人来算，在发送死亡通知时，死亡的人可能收不到死亡消息，造成有可能继续留在地图中一段时间 ChenXiaojun                   
                    int roleNum = EnterBattleClientCount - AllKilledRoleNum;
                    if (GameManager.ClientMgr.GetMapClientsCount(MapCode) <= 1)
                    {
                        // 地图人数与统计剩余人数不一致时记日志 ChenXiaojun
                        int nMapRoleNum = GameManager.ClientMgr.GetMapClientsCount(MapCode);
                        if (roleNum != nMapRoleNum)
                        {
                            String strLog = String.Format("PK之王活动中地图人数({0})与统计剩余人数({1})不一致", nMapRoleNum, roleNum);
                            LogManager.WriteLog(LogTypes.Error, strLog);
                        }

                        //是否允许攻击
                        AllowAttack = false;

                        BattlingState = BattleStates.EndFight;
                        StateStartTicks = DateTime.Now.Ticks / 10000;
                    }
                }
                else
                {
                    //定时通知在场的玩家战斗状态
                    ProcessTimeNotifyBattleKilledNum();

                    //定时给在场的玩家增加经验
                    ProcessTimeAddRoleExp();
                }
            }
            else if (BattlingState == BattleStates.EndFight) //结束战斗(此时伤害无效)
            {
                BattlingState = BattleStates.ClearBattle;
                StateStartTicks = DateTime.Now.Ticks / 10000;

                //向血战地府场景内的角色发送战斗清场倒计时消息
                //发送广播消息
                GameManager.ClientMgr.NotifyArenaBattleInviteMsg(Global._TCPManager.MySocketListener, Global._TCPManager.TcpOutPacketPool, MapCode, (int)BattleCmds.Time, (int)BattleStates.ClearBattle, ClearRolesSecs);

                //开始计算给予的奖励
                /// 处理血战地府结束时的奖励
                ProcessBattleResultAwards();
            }
            else if (BattlingState == BattleStates.ClearBattle) //清空战斗场景
            {
                //如果超过了最大倒计时时间，允许角色进入战斗伤害
                long ticks = DateTime.Now.Ticks / 10000;
                if (ticks >= (StateStartTicks + (ClearRolesSecs * 1000)))
                {
                    //强迫将所有逗留的用户强制传送出去
                    GameManager.ClientMgr.NotifyBattleLeaveMsg(Global._TCPManager.MySocketListener, Global._TCPManager.TcpOutPacketPool, MapCode);

                    BattlingState = BattleStates.NoBattle;
                    StateStartTicks = 0;

                    // 清空 [4/2/2014 LiaoWei]
                    TheKingOfPKGetawardFlag.Clear();
                }
            }
        }

        /// <summary>
        /// 处理非战斗期间的逻辑
        /// </summary>
        private void ProcessNoBattle()
        {
            //判断是否要开始血战地府
            if (!JugeStartBattle())
            {
                return;
            }

            //当前总的在线人数是否够？
            //if (GameManager.ClientMgr.GetClientCount() < MinRequestNum)
            //{
            //    return;
            //}

            //战斗开始前的人数
            StartRoleNum = 0;

            //当前总的在线用户数
            TotalClientCount = 0;

            //已经杀死的角色的人数
            _AllKilledRoleNum = 0;

            lock (DeadRoleSets)
            {
                DeadRoleSets.Clear();
            }

            // 强制清场
            GameManager.ClientMgr.BattleBeginForceLeaveg(Global._TCPManager.MySocketListener, Global._TCPManager.TcpOutPacketPool, MapCode);

            //广播血战地府的消息给在线用户
            BattlingState = BattleStates.PublishMsg;

            //发送广播消息
            GameManager.ClientMgr.NotifyAllArenaBattleInviteMsg(Global._TCPManager.MySocketListener, Global._TCPManager.TcpOutPacketPool, MinLevel, (int)BattleCmds.Invite, (int)BattleStates.PublishMsg, WaitingEnterSecs);

            // 不同状态的开始时间
            StateStartTicks = DateTime.Now.Ticks / 10000;
        }

        /// <summary>
        /// 判断是否需要立刻开始血战地府
        /// </summary>
        /// <returns></returns>
        private bool JugeStartBattle()
        {
            string nowTime = DateTime.Now.ToString("HH:mm");
            List<string> timePointsList = TimePointsList;
            if (null == timePointsList) return false;

            for (int i = 0; i < timePointsList.Count; i++)
            {
                if (timePointsList[i] == nowTime)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 剩余次数
        /// </summary>
        /// <returns></returns>
        public int LeftEnterCount()
        {
            int count = 0;
            string nowTime = DateTime.Now.ToString("HH:mm");
            List<string> timePointsList = TimePointsList;
            if (null == timePointsList) return 0;
            try
            {
                for (int i = 0; i < timePointsList.Count; i++)
                {
                    DateTime tt = DateTime.Parse(timePointsList[i]);
                    tt.AddMinutes(ClearRolesSecs);
                    if (tt >= DateTime.Now)
                    {
                        count += 1;
                    }
                }
            }
            catch (Exception e)
            {
                LogManager.WriteException(e.ToString());
            }
            return count;
        }
        #endregion 处理方法

        #region 供外部访问的线程安全变量和方法

        /// <summary>
        /// 供外部访问的线程锁对象
        /// </summary>
        private object mutex = new object();

        /// <summary>
        /// 最高积分
        /// </summary>
        public int TheKingOfPKTopPoint
        {
            get { return TopPoint; }
            set { TopPoint = value; }
        }

        /// <summary>
        /// 最高积分
        /// </summary>
        public string TheKingOfPKTopRoleName
        {
            get { return TopRoleName; }
            set { TopRoleName = value; }
        }

        /// <summary>
        /// 玩家领取奖励的状态
        /// </summary>
        public Dictionary<int, int> TheKingOfPKGetawardFlag
        {
            get { return GetawardFlag; }
            set { GetawardFlag = value; }
        }

        /// <summary>
        /// 血战地府的场景编号
        /// </summary>
        public int BattleMapCode
        {
            get { return MapCode; }
        }

        /// <summary>
        /// 血战地府的连续编号
        /// </summary>
        public int BattleServerLineID
        {
            get { return BattleLineID; }
        }

        /// <summary>
        /// 是否允许了进入场景
        /// </summary>
        public bool AllowEnterMap
        {
            get
            {
                //开始战斗之后不允许再次进入
                return (BattlingState >= BattleStates.PublishMsg && BattlingState < BattleStates.StartFight);
            }
        }

        public bool IsFighting
        {
            get
            {
                //开始战斗之后不允许再次进入
                return (BattlingState >= BattleStates.StartFight && BattlingState < BattleStates.ClearBattle);
            }
        }

        private bool _AllowAttack = false;

        /// <summary>
        /// 是否允许攻击
        /// </summary>
        public bool AllowAttack
        {
            get { lock (mutex) { return _AllowAttack; } }
            set { lock (mutex) { _AllowAttack = value; } }
        }

        /// <summary>
        /// 允许的最低级别
        /// </summary>
        public int AllowMinChangeLifeLev
        {
            get { return MinChangeLifeLev; }
        }

        /// <summary>
        /// 允许的最低级别
        /// </summary>
        public int AllowMinLevel
        {
            get { return MinLevel; }
        }

        private int _TotalClientCount = 0;

        /// <summary>
        /// 当前总的在线用户数
        /// </summary>
        public int TotalClientCount
        {
            get { lock (mutex) { return _TotalClientCount; } }
            set { lock (mutex) { _TotalClientCount = value; } }
        }

        private int _EnterBattleClientCount = 0;

        /// <summary>
        /// 进入地图的在线用户数
        /// </summary>
        public int EnterBattleClientCount
        {
            get { lock (mutex) { return _EnterBattleClientCount; } }
            set { lock (mutex) { _EnterBattleClientCount = value; } }
        }

        /// <summary>
        /// 客户端进入地图
        /// </summary>
        /// <returns></returns>
        public bool ClientEnter(GameClient client)
        {
            bool ret = false;
            lock (mutex)
            {
                if (TheKingOfPKGetawardFlag.ContainsKey(client.ClientData.RoleID))
                {
                    return true;
                }

                //当前总的在线用户数
                if (_TotalClientCount < MaxEnterNum)
                {
                    _TotalClientCount++;

                    TheKingOfPKGetawardFlag.Add(client.ClientData.RoleID, 0);

                    ret = true;
                }
            }

            ProcessTimeNotifyBattleKilledNum(true);

            string strcmd = string.Format("{0}:{1}", 0, _TotalClientCount);
            GameManager.ClientMgr.SendToClient(client, strcmd, (int)TCPGameServerCmds.CMD_SPR_ARENABATTLEKILLEDNUM);

            return ret;
        }

        /// <summary>
        /// 客户端离开地图
        /// </summary>
        /// <returns></returns>
        protected void ClientLeave(GameClient client)
        {
            lock (mutex)
            {
                //当前总的在线用户数
                _TotalClientCount--;

                TheKingOfPKGetawardFlag.Remove(client.ClientData.RoleID);
            }

            ProcessTimeNotifyBattleKilledNum(true);

        }

        /// <summary>
        /// 离开竞技场地图
        /// </summary>
        /// <param name="roleID"></param>
        /// <returns></returns>
        public void LeaveArenaBattleMap(GameClient client)
        {
            if (client.ClientData.MapCode != MapCode)
            {
                return;
            }

            // MU 给奖励 [3/22/2014 LiaoWei]
            ProcessAward(client);

            //客户端离开地图
            ClientLeave(client);
        }

        /// <summary>
        /// 进入竞技场地图
        /// </summary>
        /// <param name="roleID"></param>
        /// <returns></returns>
        public bool EnterArenaBattleMap(GameClient client)
        {
            if (client.ClientData.MapCode != MapCode)
            {
                return false;
            }

            //进入竞技场地图
            return ClientEnter(client);
        }

        /// <summary>
        /// 禁止使用的物品ID字符串
        /// </summary>
        public string BattleDisableGoodsIDs
        {
            get { return DisableGoodsIDs; }
        }

        /// <summary>
        /// 战斗开始前的人数
        /// </summary>
        private int _StartRoleNum = 0;

        /// <summary>
        /// 战斗开始前的人数
        /// </summary>
        public int StartRoleNum
        {
            get { lock (mutex) { return _StartRoleNum; } }
            set { lock (mutex) { _StartRoleNum = value; } }
        }

        /// <summary>
        /// 已经死亡的角色的人数
        /// </summary>
        private int _AllKilledRoleNum = 0;

        /// <summary>
        /// 已经死亡的角色的人数
        /// </summary>
        public int AllKilledRoleNum
        {
            get { lock (mutex) { return _AllKilledRoleNum; } }
            set
            {
                lock (mutex) { _AllKilledRoleNum = value; }

                //有人死亡，强行通知所有客户端
                ProcessTimeNotifyBattleKilledNum(true);
            }
        }

        public void SetTotalPointInfo(string sName, int nValue)
        {
            TheKingOfPKTopRoleName = sName;
            TheKingOfPKTopPoint = nValue;
        }

        /// <summary>
        /// 场景判断
        /// </summary>
        public bool IsInPkScene(int nMap)
        {
            if (nMap == (int)THEKINGOFPKINFO.THEKINGOFPKINFO_MAPCODE)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 是否是第一个击杀者(因为要在多线程环境下计死亡个数)
        /// </summary>
        /// <param name="client"></param>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool ProcessRoleDead(GameClient other)
        {
            int roleID = other.ClientData.RoleID;
            bool firstKill = false;
            lock (DeadRoleSets)
            {
                if (!DeadRoleSets.Contains(roleID))
                {
                    DeadRoleSets.Add(roleID);
                    _AllKilledRoleNum += 1;
                    firstKill = true;
                }
            }

            if (firstKill)
            {
                ProcessTimeNotifyBattleKilledNum(true);
            }

            return firstKill;
        }

        #endregion 供外部访问的线程安全变量和方法

        #region 配置和处理战斗结束时的冠军称号和buffer奖励

        /// <summary>
        /// 处理血战地府结束时的奖励
        /// </summary>
        private void ProcessBattleResultAwards()
        {
            //没人被杀死，没人是冠军,冠军要存在，起码得有人被杀死===>根据要求，即使没人被杀死，只要只剩余一个玩家，他也是pk王
            /*
            if (AllKilledRoleNum <= 0)
            {
                Global.BroadcastArenaChampionMsg(false);
                return;
            }
            */

            //先找出当前血战地府中的所有的或者的人
            List<GameClient> clientList = GameManager.ClientMgr.GetMapAliveClients(MapCode);

            //必须只剩下一个玩家
            // if (null == clientList || clientList.Count != 1)
            // 不以地图中残留的人做判断，而已统计人数作判断 ChenXiaojun
            if (null == clientList || clientList.Count != 1)
            {
                Global.BroadcastArenaChampionMsg(false);

                //回复PK之王雕像
                Global.RestorePKingNpc(GetPKKingRoleID());

                Global.UpdateDBGameConfigg(GameConfigNames.PKKingRole, "0"); //没有产生新的PK之王
                return;
            }

            GameClient championClient = null;

            List<BattleRoleItem> battleRoleItemList = new List<BattleRoleItem>();
            for (int i = 0; i < clientList.Count; i++)
            {
                GameClient c = clientList[i];
                if (c == null) continue;

                if (c.ClientData.CurrentLifeV <= 0) continue;

                //一个人没杀，不能算冠军，比如大家杀人，然后掉线，有一个人谁也没杀，他任然不是冠军
                //if (c.ClientData.ArenaBattleKilledNum > 0)
                //{
                //    championClient = c;
                //}

                // 有多个，选一个，这里不变 ChenXiaojun
                championClient = c;

                // 给奖励 [3/26/2014 LiaoWei]
                ProcessAward(c);

                break;
            }

            //无冠军
            if (null == championClient)
            {
                Global.BroadcastArenaChampionMsg(false);

                //回复PK之王雕像
                Global.RestorePKingNpc(GetPKKingRoleID());

                Global.UpdateDBGameConfigg(GameConfigNames.PKKingRole, "0"); //没有产生新的PK之王

                return;
            }

            Global.BroadcastArenaChampionMsg(true, championClient);

            //为获胜的角色增加Buffer和标识
            AddBattleBufferAndFlags(championClient);

            //设置当前的PK之王
            SetPKKingRoleID(championClient.ClientData.RoleID);

            //替换PK之王的npc显示
            Global.ReplacePKKingNpc(championClient.ClientData);

            return; //返回，不再处理
        }

        /// <summary>
        /// 为获胜的角色增加Buffer和标识
        /// </summary>
        /// <param name="client"></param>
        /// <param name="bufferType"></param>
        private void AddBattleBufferAndFlags(GameClient client)
        {
            //Buffer参数
            double[] actionParams = new double[2];
            actionParams[0] = 60.0 * 24.0 * 60 - 1200; //24个小时的buffer时间 -- 改成 23小时40分钟
            actionParams[1] = 2000800; //BUFFER 物品id

            client.ClientData.BattleNameStart = DateTime.Now.Ticks / 10000;
            client.ClientData.BattleNameIndex = 1;

            //移除攻击类型buffer
            Global.RemoveBufferData(client, (int)BufferItemTypes.TimeAddAttack);
            Global.RemoveBufferData(client, (int)BufferItemTypes.TimeAddDSAttack);
            Global.RemoveBufferData(client, (int)BufferItemTypes.TimeAddMAttack);

            //更新BufferData
            Global.UpdateBufferData(client, BufferItemTypes.PKKingBuffer, actionParams);

            //异步写数据库，写入当前的血战地府称号信息
            GameManager.DBCmdMgr.AddDBCmd((int)TCPGameServerCmds.CMD_DB_UPDATEBATTLENAME,
                string.Format("{0}:{1}:{2}", client.ClientData.RoleID, client.ClientData.BattleNameStart, client.ClientData.BattleNameIndex),
                null);

            //通知血战地府称号的信息
            GameManager.ClientMgr.NotifyRoleBattleNameInfo(Global._TCPManager.MySocketListener, Global._TCPManager.TcpOutPacketPool, client);

            //血战地府称号次数更新
            GameManager.ClientMgr.UpdateBattleNum(client, 1, false);

            //设置合服后的PK王
            HuodongCachingMgr.UpdateHeFuPKKingRoleID(client.ClientData.RoleID);
            
        }

        /// <summary>
        /// 获取当前的PK之王
        /// </summary>
        /// <returns></returns>
        public int GetPKKingRoleID()
        {
            return GameManager.GameConfigMgr.GetGameConfigItemInt(GameConfigNames.PKKingRole, 0);
        }

        /// <summary>
        /// 设置当前的PK之王
        /// </summary>
        /// <returns></returns>
        public void SetPKKingRoleID(int roleID)
        {
            // 存盘 [3/26/2014 LiaoWei]
            Global.UpdateDBGameConfigg(GameConfigNames.PKKingRole, roleID.ToString());
            GameManager.GameConfigMgr.SetGameConfigItem(GameConfigNames.PKKingRole, roleID.ToString());
        }

        #endregion  配置和处理战斗结束时的冠军称号和buffer奖励

        #region 定时[每隔30秒]通知在场的玩家竞技场剩余人数

        /// <summary>
        /// 定时通知在场玩家战斗状态，死亡多少人，剩余多少人，初始多少人，通过这些，客户端还能计算出中间自动退出多少人
        /// 战斗过程中 每次有人被杀 或者 离开，都应该通知一下
        /// </summary>
        private void ProcessTimeNotifyBattleKilledNum(bool force = false) // 改造接口 [4/1/2014 LiaoWei]
        {
            //             if (BattlingState != BattleStates.StartFight) //非战斗装备，不加经验
            //             {
            //                 return;
            //             }

            long ticks = DateTime.Now.Ticks / 10000;
            if (ticks - LastNotifyBattleKilledNumTicks < (NotifyBattleKilledNumSecs * 1000) && !force)
            {
                return;
            }

            LastNotifyBattleKilledNumTicks = ticks;

            GameManager.ClientMgr.NotifyArenaBattleKilledNumCmd(Global._TCPManager.MySocketListener, Global._TCPManager.TcpOutPacketPool, AllKilledRoleNum, StartRoleNum, TotalClientCount);
        }

        #endregion 定时通知在场的玩家双方阵营积分信息

        #region 定时给在场的玩家家经验

        /// <summary>
        /// 定时给予收益
        /// </summary>
        private long LastAddBangZhanAwardsTicks = 0;

        /// <summary>
        /// 定时给在场的玩家增加经验
        /// </summary>
        private void ProcessTimeAddRoleExp()
        {
            if (BattlingState != BattleStates.StartFight) //非战斗装备，不加经验
            {
                return;
            }

            long ticks = DateTime.Now.Ticks / 10000;
            if (ticks - LastAddBangZhanAwardsTicks < (10 * 1000))
            {
                return;
            }

            LastAddBangZhanAwardsTicks = ticks;

            //先找出当前大乱斗中的所有的或者的人
            List<Object> objsList = GameManager.ClientMgr.GetMapClients(MapCode);
            if (null == objsList) return;

            for (int i = 0; i < objsList.Count; i++)
            {
                GameClient c = objsList[i] as GameClient;
                if (c == null) continue;

                //if (c.ClientData.CurrentLifeV <= 0) continue;

                /// 处理用户的经验奖励
                BangZhanAwardsMgr.ProcessBangZhanAwards(c);
            }
        }

        /// <summary>
        /// 给奖励
        /// </summary>
        private void ProcessAward(GameClient client)
        {
            int nFlag;

            if (BattlingState < BattleStates.StartFight)
            {
                //开始战斗前离开不给奖励
                return;
            }
            else if (BattlingState == BattleStates.StartFight)
            {
                long ticks = DateTime.Now.Ticks / 10000;
                if (ticks < StateStartTicks + 1000) //进场1秒后退出才给奖励,防止卡时间点刷奖励
                {
                    return;
                }
            }

            lock (mutex)
            {
                if (!TheKingOfPKGetawardFlag.TryGetValue(client.ClientData.RoleID, out nFlag))
                    return;

                if (nFlag > 0)
                    return;

                TheKingOfPKGetawardFlag[client.ClientData.RoleID] = 1;
            }

            if (client.ClientData.KingOfPkCurrentPoint > TheKingOfPKTopPoint)
            {
                SetTotalPointInfo(client.ClientData.RoleName, client.ClientData.KingOfPkCurrentPoint);
            }

            // 1.成就=系数1+Min(系数2,积分)*系数3   2.经验=Min(400,转生级别*400+人物等级)*系数1+Min（系数2,积分）*系数3  2014.4.21改成 人物级别*转生级别对应比例*系数1 +Min（系数2,积分）*系数3
            // 经验=系数1*SystemParams.xml定义的ZhuanShengExpXiShu参数值+Min（系数2,积分）*系数3*SystemParams.xml定义的ZhuanShengExpXiShu参数值

            string strPkAward = GameManager.systemParamsList.GetParamValueByName("PkAward");
            string[] strChengJiu = null;
            string[] strExp = null;
            int nChengjiuPoint = 0;
            long nExp = 0;


            if (!string.IsNullOrEmpty(strPkAward))
            {
                string[] strFild = strPkAward.Split('|');

                string strInfo = strFild[0];

                strChengJiu = strInfo.Split(',');

                strInfo = strFild[1];

                strExp = strInfo.Split(',');
            }

            nChengjiuPoint = Global.SafeConvertToInt32(strChengJiu[0]) + Global.GMin(Global.SafeConvertToInt32(strChengJiu[1]), client.ClientData.KingOfPkCurrentPoint) *
                                    Global.SafeConvertToInt32(strChengJiu[2]);

            if (nChengjiuPoint > 0)
            {
                ChengJiuManager.AddChengJiuPoints(client, "角斗赛", nChengjiuPoint, true, true);
            }

            //nExp = Global.GMin(400, client.ClientData.ChangeLifeCount * 400 + client.ClientData.Level) * Global.SafeConvertToInt32(strExp[0]) + 
            //            Global.GMin(Global.SafeConvertToInt32(strExp[1]), client.ClientData.KingOfPkCurrentPoint) * Global.SafeConvertToInt32(strExp[2]);

            double nRate = 0.0;
            /*if (client.ClientData.ChangeLifeCount == 0)
                nRate = 1;
            else
                nRate = Data.ChangeLifeInfoList[client.ClientData.ChangeLifeCount].ExpProportion;*/

            nRate = Data.ChangeLifeEverydayExpRate[client.ClientData.ChangeLifeCount];

            //(int)(client.ClientData.Level * nRate * Global.SafeConvertToInt32(strExp[0]) + Global.GMin(Global.SafeConvertToInt32(strExp[1]), client.ClientData.KingOfPkCurrentPoint) * Global.SafeConvertToInt32(strExp[2]));
            nExp = (int)(Global.SafeConvertToInt32(strExp[0]) * nRate + Global.GMin(Global.SafeConvertToInt32(strExp[1]), client.ClientData.KingOfPkCurrentPoint) * Global.SafeConvertToInt32(strExp[2]) * nRate);

            double dblExperience = 1.0;
            // 合服期间奖励翻倍
            // 活动时间内
            HeFuAwardTimesActivity activity = HuodongCachingMgr.GetHeFuAwardTimesActivity();
            if (null != activity && activity.InActivityTime() && activity.activityTimes > 0.0)
            {
                dblExperience += activity.activityTimes - 1;
            }
            // 节日多倍奖励
            JieRiMultAwardActivity jieriact = HuodongCachingMgr.GetJieRiMultAwardActivity();
            if (null != jieriact)
            {
                JieRiMultConfig config = jieriact.GetConfig((int)MultActivityType.TheKingOfPK);
                if (null != config)
                {
                    dblExperience += config.GetMult();
                }
            }


            nExp = (int)(nExp * dblExperience);

            if (nExp > 0)
            {
                GameManager.ClientMgr.ProcessRoleExperience(client, nExp);
            }

            string strCmd = "";
            // 1.自己的积分 2.获得成就积分 3.获得经验
            strCmd = string.Format("{0}:{1}:{2}", client.ClientData.KingOfPkCurrentPoint, nChengjiuPoint, nExp);

            client.ClientData.KingOfPkCurrentPoint = 0;

            GameManager.ClientMgr.SendToClient(client, strCmd, (int)TCPGameServerCmds.CMD_SPR_NOTIFYTHEKINGOFPKAWARDINFO);
        }

        #endregion 定时给在场的玩家家经验

        #region 服务器启动后恢复PK之王的显示

        /// <summary>
        /// 重新恢复显示PK之王
        /// </summary>
        public void ReShowPKKing()
        {
            int roleID = GetPKKingRoleID();
            if (roleID <= 0)
            {
                return;
            }

            SafeClientData clientData = Global.GetSafeClientDataFromLocalOrDB(roleID);
            if (null != clientData)
            {
                //替换PK之王的npc显示
                Global.ReplacePKKingNpc(clientData);
            }
        }

        #endregion 服务器启动后恢复PK之王的显示
    }
}
