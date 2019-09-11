using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading;

namespace DeviceOnline
{
    /** 
    * @desc Pinger
    * -Scheduler, seperate thread used to ping devices
    * class PingDevice, store information about 1 device from the database
    * class PingGroup
    * -PingGroup id,frequeny, sets up and waits to ping a group
    * -BeginPingGroup, actually pings thr devices in a group
    * @author Martin Sykes admin@martrinex.net
    */
    public class Pinger{
        public void Scheduler()
        {
            while (Program.running)
            {
                // loop groups and setup to ping groups
                // only get groups where nextping is less than current time
                SQLiteCommand sql = new SQLiteCommand("SELECT `name`,`pingfrequency`,`rowid` FROM `groups` WHERE `nextping` IS NULL OR `nextping` < CURRENT_TIMESTAMP", DB.db);
                SQLiteDataReader r = sql.ExecuteReader();
                while (r.Read())
                {
                    // ready to ping so get all details and prepare a group
                    int pingfrequency = r.GetInt32(1);
                    int rowid = r.GetInt32(2);
                    string sqli = "UPDATE `groups` SET `nextping`=datetime(CURRENT_TIMESTAMP,'+" + pingfrequency + " minutes') WHERE  `rowid`=" + rowid;
                    new SQLiteCommand(sqli, DB.db).ExecuteNonQuery();
                    Console.WriteLine("Need to ping group: '" + r.GetString(0)+"'");
                    new PingGroup(rowid, pingfrequency);
                }
                r.Close();
                Thread.Sleep(15000);
            }
            Console.WriteLine("Scheduler stopped.");
        }
    }

    // store/cache information about 1 device
    public class PingDevice
    {
        public string name, notifyemail;
        public bool online = false, reported = false;
        public bool lastKnownOnlineStatus = false; // 0.92 to enable emails when devices comes back online
        public int rowid, tries = 0, periodid, alerttime, pingfrequency;
        public PingDevice(int rowid, string name, int periodid, int alerttime, string notifyemail, int pingfrequency, bool reported, bool lastKnownOnlineStatus)
        {
            this.rowid = rowid; this.name = name;
            this.periodid = periodid; this.alerttime = alerttime;
            this.notifyemail = (notifyemail == "") ? null : notifyemail;
            this.pingfrequency = pingfrequency;
            this.reported = reported;
            this.lastKnownOnlineStatus = lastKnownOnlineStatus;
        }
    }

    public class PingGroup
    {
        public int groupid;
        public List<PingDevice> pingList = new List<PingDevice>();
        public bool working = true;

        // setup computers to ping, then create new thread to actually ping them
        public PingGroup(int groupid, int pingfrequency)
        {
            this.groupid = groupid;
            // find out what period we are in, get details of who to notify / when etc
            string day = DateTime.Now.DayOfWeek.ToString().Substring(0, 3); if (day == "Thu") day = "Thur";
            int frequency = Convert.ToInt32(new SQLiteCommand("SELECT `pingfrequency` FROM `groups` WHERE `rowid`=" + groupid, DB.db).ExecuteScalar());
            string notifyemail = Convert.ToString(new SQLiteCommand("SELECT `mailto` FROM `groups` WHERE `rowid`=" + groupid, DB.db).ExecuteScalar());
            SQLiteDataReader reader = new SQLiteCommand("SELECT `alertmins`,`periodid` FROM `groupperiods`,`periods` WHERE `periods`.`rowid`=`groupperiods`.`periodid` AND `groupid`=" + groupid + " AND `" + day + "`=True AND ((`start`<=time(datetime('now','localtime')) AND `end`>time(datetime('now','localtime'))) OR `periodid`=1) ORDER BY `periodid` DESC LIMIT 1", DB.db).ExecuteReader();
            reader.Read();
            int alertmins = reader.GetInt32(0), periodid = reader.GetInt32(1);
            reader.Close();
            Console.WriteLine("mins:" + alertmins + ",period:" + periodid);
            // get all the devices to ping
            SQLiteCommand sql = new SQLiteCommand("SELECT `rowid`,`name`,`reported`,`online` FROM `devices` WHERE `groupid`=" + groupid, DB.db);
            SQLiteDataReader r = sql.ExecuteReader();
            while (r.Read()) pingList.Add(new PingDevice(r.GetInt32(0), r.GetString(1), periodid, alertmins, notifyemail, pingfrequency, r.GetBoolean(2),r.GetBoolean(3)));
            r.Close();
            // start pinging them in the new thread
            try { new Thread(BeginPingGroup).Start(); } catch { }
        }

        // ping all devices cache in the group
        public void BeginPingGroup()
        {
            System.Net.NetworkInformation.Ping pinger = new System.Net.NetworkInformation.Ping();
            string sql = "";
            bool work = true;
            // try 3 times (rather do a quick ping then 2 subsequent slower ones)
            for (int t = 0; t < 3 && work; t++)
            {
                work = false; sql = "";
                foreach (PingDevice device in pingList)
                {
                    // all devices start as offline as we go round we start skipping any what are seen online
                    if (!device.online)
                    {
                        try
                        {
                            System.Net.NetworkInformation.PingReply reply = pinger.Send(device.name, (t == 0) ? 500 : 3000);
                            device.online = (reply.Status == System.Net.NetworkInformation.IPStatus.Success);
                        }
                        catch { }
                        if (device.online)
                        {
                            if(device.reported && !device.lastKnownOnlineStatus)
                            {
                                //TODO: notify a server coming back online
                                Program.notifications.AddNotification(device.rowid, device.name, device.notifyemail, 0, true);
                            }
                            // new device reporting online, cache sql block to update the db
                            sql += "UPDATE `devices` SET `online`=TRUE,`reported`=FALSE, `lastonline`=CURRENT_TIMESTAMP WHERE `rowid`=" + device.rowid + ";";
                            object orowid = (Program.running) ? new SQLiteCommand("SELECT `rowid` FROM `uptime` WHERE `deviceid`=" + device.rowid + " AND `day`=DATE() AND `period`=" + device.periodid, DB.db).ExecuteScalar() : null;
                            if (orowid == null)
                            {
                                sql += "INSERT INTO `uptime` (`deviceid`,`day`,`period`,`minutesonline`,`minutesoffline`) VALUES (" + device.rowid + ",DATE()," + device.periodid + "," + device.pingfrequency + ",0);";
                            }
                            else
                            {
                                sql += "UPDATE `uptime` SET `minutesonline`=`minutesonline`+" + device.pingfrequency + " WHERE `rowid`=" + Convert.ToInt32(orowid) + ";";
                            }
                        }
                        else work = true;
                    }
                }
                // end of a loop, run sql batch updates for online computers.
                if (sql != "" && Program.running) new SQLiteCommand(sql, DB.db).ExecuteNonQuery();
                if (work) Thread.Sleep(100);
            }
            // done our pinging, for any devices not online: write down all offline devicesin the group
            if (work)
            {
                sql = "";
                foreach (PingDevice device in pingList)
                {
                    if (!device.online)
                    {
                        sql += "UPDATE `devices` SET `online`=FALSE WHERE `rowid`=" + device.rowid + ";";
                        object orowid = (Program.running) ? new SQLiteCommand("SELECT `rowid` FROM `uptime` WHERE `deviceid`=" + device.rowid + " AND `day`=DATE() AND `period`=" + device.periodid, DB.db).ExecuteScalar() : null;
                        if (orowid == null)
                        {
                            sql += "INSERT INTO `uptime` (`deviceid`,`day`,`period`,`minutesonline`,`minutesoffline`) VALUES (" + device.rowid + ",DATE()," + device.periodid + ",0," + device.pingfrequency + ");";
                        }
                        else
                        {
                            sql += "UPDATE `uptime` SET `minutesoffline`=`minutesoffline`+" + device.pingfrequency + " WHERE `rowid`=" + Convert.ToInt32(orowid) + ";";
                        }
                        // setup notification for devices offline, the notification class keeps trying to contact the device to avoid spamming recipients.
                        if (device.notifyemail != null && !device.reported)
                        {
                            Program.notifications.AddNotification(device.rowid, device.name, device.notifyemail, device.alerttime,false);
                        }
                    }
                }
                // batch write offline computers to db
                if (sql != "" && Program.running) new SQLiteCommand(sql, DB.db).ExecuteNonQuery();
            }
        }
    }
}
