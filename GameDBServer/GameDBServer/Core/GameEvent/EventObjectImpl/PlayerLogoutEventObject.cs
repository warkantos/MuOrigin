﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Server.Data;
using GameDBServer.DB;

namespace GameDBServer.Core.GameEvent.EventObjectImpl
{
    /// <summary>
    /// Players log out events
    /// </summary>
    public class PlayerLogoutEventObject : EventObject
    {
        private DBRoleInfo roleInfo;

        public PlayerLogoutEventObject(DBRoleInfo roleInfo)
            : base((int)EventTypes.PlayerLogout)
        {
            this.roleInfo = roleInfo;
        }

        public DBRoleInfo RoleInfo
        {
            get { return this.roleInfo; }
        }
    }
}
