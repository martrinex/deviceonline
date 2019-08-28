using System;
using System.Data.SQLite;
using System.Net.Mail;
using System.Text;
using System.Security.Cryptography;

namespace DeviceOnline
{
    /** 
    * @desc DB, sets up SQLite database, creates tables if they don't exist.
    * -initSettings, sets up smtp client
    * -getSetting, quick way to read the settings table
    * -setSetting, quick way to read the settings table 
    * @author Martin Sykes admin@martrinex.net
    */
    class DB
    {
        public static SQLiteConnection db;
         /* TABLES
         * ------
         * settings:      -key,-value
         * users:         -username,-password,-laston,-email
         * groups:        -id,-pingfrequency,-mailto,-nextping,-dashboardid,-reported
         * devices:       -id,-groupid,-name,-online,-lastonline,-reported
         * uptime:        -deviceid,-day,-period,-minutesup,-minutesdown
         * periods:       -name,-mon,-tue,-wed,-thur,-fri,-sat,-sun,-start,-end
         * groupperiods: -groupid, -periodid, -alertmins
         */

        public DB()
        {
            Console.WriteLine("Connecting sqllite database");
            db = new SQLiteConnection("Data Source="+Program.path+"db.sqlite;Version=3;");
            db.Open();

            int r;
            String sqlCreateSettings = "CREATE TABLE IF NOT EXISTS `Settings` (`Key` TEXT PRIMARY KEY,`Value` TEXT) WITHOUT ROWID; ";
            Console.WriteLine("Setup table `settings`: " + new SQLiteCommand(sqlCreateSettings, db).ExecuteNonQuery());
            String sqlCreateGroups = "CREATE TABLE IF NOT EXISTS `Groups` (`name` TEXT NOT NULL UNIQUE, `pingfrequency` INT, `mailto` TEXT DEFAULT NULL, `nextping` DATETIME DEFAULT NULL, `dashboardid` INT DEFAULT 0);";
            Console.WriteLine("Setup table `groups`: " + new SQLiteCommand(sqlCreateGroups, db).ExecuteNonQuery());
            String sqlCreateDevices = "CREATE TABLE IF NOT EXISTS `Devices` (`groupid` INT,`name` TEXT NOT NULL, `online` BOOL, `lastonline` DATETIME, `reported` BOOL DEFAULT False);";
            Console.WriteLine("Setup table `devices`: " + (r = new SQLiteCommand(sqlCreateDevices, db).ExecuteNonQuery()));
            String sqlCreateUptime = "CREATE TABLE IF NOT EXISTS `Uptime` (`deviceid` INT,`period` INT, `day` DATE NOT NULL, minutesonline INT DEFAULT 0, minutesoffline INT DEFAULT 0);";
            Console.WriteLine("Setup table `uptime`: " + (r = new SQLiteCommand(sqlCreateUptime, db).ExecuteNonQuery()));
            String sqlCreateUsers = "CREATE TABLE IF NOT EXISTS `Users` (`username` TEXT PRIMARY KEY, `password` TEXT, `email` TEXT, `laston` DATETIME);";
            Console.WriteLine("Setup table `users`: " + (r = new SQLiteCommand(sqlCreateUsers, db).ExecuteNonQuery()));
            if (r != -1) new SQLiteCommand("INSERT INTO `users` (`username`,`password`,`email`) VALUES ('admin','" + Program.GetHashString("admin") + "','');", db).ExecuteNonQuery();
            String sqlCreatePeriods = "CREATE TABLE IF NOT EXISTS `Periods` (`name` TEXT, `mon` BOOL, `tue` BOOL, `wed` BOOL, `thur` BOOL, `fri` BOOL,`sat` BOOL,`sun` BOOL,`start` TIME, `end` TIME);";
            Console.WriteLine("Setup table `periods`: " + (r = new SQLiteCommand(sqlCreatePeriods, db).ExecuteNonQuery()));
            if (r != -1) new SQLiteCommand("INSERT INTO `periods` (`name`,`mon`,`tue`,`wed`,`thur`,`fri`,`sat`,`sun`,`start`,`end`) VALUES ('default',true,true,true,true,true,true,true,'00:00','00:00');", db).ExecuteNonQuery();
            String sqlCreateGroupPeriods = "CREATE TABLE IF NOT EXISTS `GroupPeriods` (`groupid` INT , `periodid` INT, `alertmins` INT DEFAULT 30);";
            Console.WriteLine("Setup table `group periods`: " + (r = new SQLiteCommand(sqlCreateGroupPeriods, db).ExecuteNonQuery()));
            Console.WriteLine("Database connected.");
            string sql = "select count(*) from `devices`";
            SQLiteCommand command = new SQLiteCommand(sql, db);
            int count = Convert.ToInt32(command.ExecuteScalar());
            Console.WriteLine("Total devices:" + count);
            if (count == 0)
            {
                Console.WriteLine("Test group addition: " + new SQLiteCommand("INSERT INTO `groups` (`name`,`pingfrequency`) VALUES ('test',30)", db).ExecuteNonQuery());
                int row = Convert.ToInt32(new SQLiteCommand("SELECT rowid FROM groups WHERE `name`='test'", db).ExecuteScalar());
                Console.WriteLine("Test group periods addition: " + new SQLiteCommand("INSERT INTO `groupperiods` (`groupid`,`periodid`) VALUES ("+row+",1)", db).ExecuteNonQuery());
                Console.WriteLine("Group row: " + row);
                Console.WriteLine("Test device addition: " + new SQLiteCommand("INSERT INTO `devices` (`groupid`,`name`,`online`,`lastonline`) VALUES (" + row + ",'localhost',false,NULL)", db).ExecuteNonQuery());
            }
            string lastVersion = getSetting("dbversion");
            if (lastVersion != Program.version + "")
            {
                //TODO: database updates
                setSetting("dbversion", Program.version + "");
            }
        }

        public static void initSettings()
        {
            string mailhost = getSetting("mailhost");
            string mailport = getSetting("mailport");
            string mailuser = getSetting("mailusername");
            string mailpass = getSetting("mailpassword");
            bool mailssl = (getSetting("mailssl") == "true") ? true : false;

            if (mailport == "") mailport = "25";
            if (mailhost != "")
            {
                Notifications.smtp = new SmtpClient(mailhost);
                Notifications.smtp.Port = Convert.ToInt32(mailport);
                mailpass = Encoding.Unicode.GetString(ProtectedData.Unprotect(Convert.FromBase64String(mailpass), Encoding.UTF8.GetBytes(Program.salt), DataProtectionScope.CurrentUser));
                Notifications.smtp.Credentials = new System.Net.NetworkCredential(mailuser, mailpass);
                Notifications.smtp.EnableSsl = mailssl;
            }
            else Notifications.smtp = null;

            string password = Program.GetHashString("admin");
            int r = new SQLiteCommand("UPDATE `users` SET `laston`=CURRENT_TIMESTAMP WHERE `username`='admin' AND `password`='" + password + "'", db).ExecuteNonQuery();
            Program.defaultPassWarning = (r == 1);

        }

        public static string getSetting(string name)
        {
            string value = Convert.ToString(new SQLiteCommand("SELECT `value` FROM `settings` WHERE `key`='" + name + "';", db).ExecuteScalar());
            return (value == null ? "" : value);
        }
        public static void setSetting(string name, string value)
        {
            new SQLiteCommand("INSERT OR REPLACE INTO `settings` (`key`,`value`) VALUES('" + name + "','" + value + "'); ", db).ExecuteNonQuery();
        }
    }
}
