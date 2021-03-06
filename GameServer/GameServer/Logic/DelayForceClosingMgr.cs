﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using Server.Protocol;
using Server.TCP;
using Server.Tools;

namespace GameServer.Logic
{
    /// <summary>
    /// 延迟强制关闭
    /// </summary>
    public class DelayForceClosingMgr
    {
        /// <summary>
        /// 记录要延迟关闭的TMSKSocket和用户ID信息的字典
        /// </summary>
        private static Dictionary<TMSKSocket, long> _Socket2UDict = new Dictionary<TMSKSocket, long>();

        /// <summary>
        /// 加入延迟关闭的TMSKSocket信息
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="UserID"></param>
        public static void AddDelaySocket(TMSKSocket socket)
        {
            long ticks = DateTime.Now.Ticks / 10000;
            lock (_Socket2UDict)
            {
                if (!_Socket2UDict.ContainsKey(socket))
                {
                    _Socket2UDict[socket] = ticks;
                }
            }
        }

        /// <summary>
        /// 删除延迟关闭的TMSKSocket信息
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="UserID"></param>
        public static void RemoveDelaySocket(TMSKSocket socket)
        {
            lock (_Socket2UDict)
            {
                _Socket2UDict.Remove(socket);
            }
        }

        /// <summary>
        /// 找出超时的TMSKSocket列表
        /// </summary>
        /// <returns></returns>
        private static List<TMSKSocket> GetDelaySockets()
        {
            List<TMSKSocket> socketsList = new List<TMSKSocket>();
            long ticks = DateTime.Now.Ticks / 10000;
            long lastTicks = 0;
            lock (_Socket2UDict)
            {
                foreach (var socket in _Socket2UDict.Keys)
                {
                    lastTicks = _Socket2UDict[socket];
                    if (ticks - lastTicks >= (3 * 1000)) //超过3秒还没关闭
                    {
                        socketsList.Add(socket);
                    }
                }
            }

            return socketsList;
        }

        /// <summary>
        /// 处理超时为关闭的连接，清空用户数据
        /// </summary>
        public static void ProcessDelaySockets()
        {
            List<TMSKSocket> socketsList = GetDelaySockets();
            if (socketsList.Count <= 0) return;

            GameClient client = null;
            for (int i = 0; i < socketsList.Count; i++)
            {
                client = GameManager.ClientMgr.FindClient(socketsList[i]);
                if (null != client)
                {
                    LogManager.WriteLog(LogTypes.Error, string.Format("RoleID={0}, RoleName={1} 延迟后强制关闭操作", client.ClientData.RoleID, client.ClientData.RoleName));
                }
                else
                {
                    LogManager.WriteLog(LogTypes.Error, string.Format("延迟后强制关闭操作, 无角色的TMSKSocket连接"));
                }

                Global.ForceCloseSocket(socketsList[i]);
            }
        }
    }
}
