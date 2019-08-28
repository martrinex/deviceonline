using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Net.Mail;
using System.Threading;

namespace DeviceOnline
{
    /** 
    * @desc Notifications
    * -AddNotification, new offline device to wait and send an email if it remains offline.
    * -Start, services what loops through devices, checks if they came online, if not and the delay expires, send an email
    * @author Martin Sykes admin@martrinex.net
    */
    class Notifications
    {
        public static SmtpClient smtp;
        private List<Notify> devices = new List<Notify>();

        // add offline device
        public void AddNotification(int deviceid, string devicename, string notifyemail, int delay)
        {
            Console.WriteLine("## New Notify:" + devicename + "," + delay);
            // check if similar notification, but this new one has a shorter time (shorter warning times setup for same device by user)
            bool found = false;
            foreach (Notify device in devices)
            {
                if (device.device == devicename && device.notifyemail == notifyemail && device.timeout > DateTime.Now.Ticks + (delay * 1000))
                {
                    device.timeout = DateTime.Now.Ticks + (delay * 1000);
                    found = true;
                }
            }
            // if no similar notifications create a new one
            if (!found) devices.Add(new Notify(deviceid, devicename, notifyemail, delay));
        }

        // service what checks all devices if they are still offline by the timeout then an email is sent
        internal void Start()
        {
            System.Net.NetworkInformation.Ping pinger = new System.Net.NetworkInformation.Ping();
            while (Program.running)
            {
                for (int d = 0; d < devices.Count; d++)
                {
                    Notify device = devices[d];
                    bool online = false;
                    try
                    {
                        System.Net.NetworkInformation.PingReply reply = pinger.Send(device.device, 1000);
                        online = (reply.Status == System.Net.NetworkInformation.IPStatus.Success);
                    }
                    catch { }
                    if (online)
                    {
                        if (Program.running) new SQLiteCommand("UPDATE `devices` SET `online`=TRUE WHERE `rowid`=" + device.deviceid, DB.db).ExecuteNonQuery();
                        devices.RemoveAt(d--);
                    }
                    else if (device.timeout < DateTime.Now.Ticks)
                    {
                        //TODO: email (5 second combine)
                        if (Notifications.smtp == null)
                        {
                            Console.WriteLine("SMTP server not setup.");
                            break;
                        }
                        MailMessage mail = new MailMessage();
                        mail.From = new MailAddress(DB.getSetting("mailusername"));
                        string[] e = device.notifyemail.Split(',');
                        foreach (string email in e) mail.To.Add(email.Trim());
                        mail.Subject = "Device Offline";
                        mail.Body = "'" + device.device + "' is offline and not responding to pings.";
                        smtp.Send(mail);
                        Console.WriteLine("Mail sent");
                        if (Program.running) new SQLiteCommand("UPDATE `devices` SET `reported`=TRUE WHERE `rowid`=" + device.deviceid, DB.db).ExecuteNonQuery();
                        devices.RemoveAt(d--);
                    }
                }
                Thread.Sleep(1000);
            }
        }
    }

    /** 
    * @desc Notify, holds information about an offline device
    * @author Martin Sykes admin@martrinex.net
    */
    public class Notify
    {
        public int deviceid;
        public string device;
        public string notifyemail;
        public long timeout;

        public Notify(int deviceid, string device, string notifyemail, int delay)
        {
            this.deviceid = deviceid;
            this.device = device;
            this.notifyemail = notifyemail;
            timeout = DateTime.Now.Ticks + (delay * 1000);
        }
    }
}
