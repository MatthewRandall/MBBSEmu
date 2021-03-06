﻿using System.Data.Common;

namespace MBBSEmu.Database.Session
{
    public interface ISessionBuilder
    {
        DbConnection GetConnection();

        DbConnection GetConnection(string connectionString);
    }
}
