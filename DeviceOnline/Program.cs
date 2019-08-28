using System;
using System.ServiceProcess;
using System.Text;
using System.Security.Cryptography;
using System.Threading;

namespace DeviceOnline
{
    /// <summary>
    /// Device Online,
    /// 
    /// Enterprise pinging application with able to run a miniwebserver and as a service.
    /// Pings devices on a network, able to email / report if/when devices go down.
    /// User can configure this application using the webserver.
    /// 
    /// Version 0.9
    /// -First test release.
    /// 
    /// ToDo list:
    /// -need to merge emails over a 5 second period
    /// -need to email when server comes back online
    /// -need to re-setup next ping when group ping frequency changes.
    /// -need a test email button
    /// -need to restrict / validate user input on forms
    /// -need to enable NTLM option for login
    /// -need transparency on pie chart background
    /// </summary>
    /// 
    /** 
    * @desc Program, entry point / start of service and console app.
    * -Main entry start program
    * -Start(userInterface) start the actual services.
    * -GetHashString(string) hash/encode data.
    * @author Martin Sykes admin@martrinex.net
    */
    static class Program
    {
        public static bool running = false;
        public static string salt = "7pHBGTFNeWQBqFuqp6JsZa@w?=";
        public static bool defaultPassWarning = false;        
        public static String prefix = "deviceonline";
        public static Notifications notifications;
        public static string path = "";
        public static float version = 0.9F;

        static void Main()
        {
            if (!Environment.UserInteractive) {
                // Start from Service
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new DeviceOnline()
                };
                ServiceBase.Run(ServicesToRun);
            }
            else
            {
                // Start console app
                Start(true);
            }
        }

        // Called from servicestart (service) and from main (interactive console app), this starts the program services.
        public static void Start(bool interactive)
        {
            // open the connection:
            //makecert -n "CN=DeviceOnline" -r -sv DeviceOnline.pvk DeviceOnline.cer
            //makecert -sk DeviceOnlineSignedCA -iv DeviceOnline.pvk -n "CN=DeviceOnline" -ic DeviceOnline.cer DeviceOnlineSignedCA.cer -sr localmachine -ss My
            //netsh http add sslcert ipport=0.0.0.0:443 certhash=ed2e05abf7f2fa71b4ed09d9659b86b95396a431 appid={7aca7ff6-16fd-462f-85d1-0c173da38d75}
            //appid get from c# studio in properties\assemblyinfo.cs
            //certhash get from ssl certificate by double clicking and details in mmc certificates

            running = true;
            // Get base path to find all web files / store db
            path = AppDomain.CurrentDomain.BaseDirectory;
            Console.WriteLine(path);
            // Start sqlite db, setup tables if they not already setup
            Console.WriteLine("Loading database.");
            new DB();
            // Start webserver
            Console.WriteLine("Initialising Socket Server.");
            WebServer web = new WebServer();            
            Console.WriteLine("Server now listening for web connections.");
            new Thread(web.Server).Start();
            // Start notification service (sends emails if devices stay offline)
            Console.WriteLine("Starting Notification Server.");
            new Thread((notifications = new Notifications()).Start).Start();
            // Start pinging groups of pc's to see if they are online
            Console.WriteLine("Starting scheduler.");
            new Thread(new Pinger().Scheduler).Start();
            // Load settings like the mail settings
            DB.initSettings();
            // if we are not a service (running in console) give the user a quit option
            if (interactive) {
                Console.WriteLine("--------------------");
                Console.WriteLine("Server is now running, press 'Q' to close server.");
                Console.WriteLine("--------------------");
                Console.WriteLine("Init Settings");
                while (Console.ReadKey().Key != ConsoleKey.Q) { }
                // user hit 'q' start shutting down (the service does this code onstop)
                running = false;
                Console.WriteLine("--------------------");
                Console.WriteLine("Server shutdown requested");
                Console.WriteLine("Stopping new connections.");
                WebServer.listener.Stop();
                Thread.Sleep(1000);
                Console.WriteLine("Stopped listening for new connections.");
                Console.WriteLine("Stopping database");
                DB.db.Close();
                Thread.Sleep(1000);
                Console.WriteLine("Database connection closed.");
                Console.WriteLine("Bye (may take 15 seconds)");
            }
        }

        // hash out passwords and other things, we don't store plain text passes
        public static string GetHashString(string inputString)
        {
            inputString += salt;
            StringBuilder sb = new StringBuilder();
            HashAlgorithm algorithm = SHA256.Create();
            byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
            foreach (byte b in hash) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}
