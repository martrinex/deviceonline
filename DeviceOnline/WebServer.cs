using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;

namespace DeviceOnline
{
    /** 
    * @desc WebServer listens for incoming requests from a web browser, sets up a session using cookies
    * -WebServer, sets up the httplistener
    * -Server, loop what accepts web requests
    * -ListenerCallback, web request sets up re-uses a session and, moves on to Session.ClientRequest to actually send a reply
    * -BakePie, createa  picture of a very simple pie chart
    * @author Martin Sykes admin@martrinex.net
    */
    class WebServer
    {
        public static HttpListener listener;
        // basically holds a username for anyone logged in
        private Dictionary<string, Session> sessions = new Dictionary<string, Session>();

        // start listining for web requests
        public WebServer()
        {
            listener = new HttpListener();
            //listener.AuthenticationSchemes = AuthenticationSchemes.Ntlm;
            //listener.UnsafeConnectionNtlmAuthentication = true;
            //listener.IgnoreWriteExceptions = true;
            //for non admins need to reserve the url to the user running the service: (command promp admin)
            //netsh http add urlacl url=https://+:443/deviceonline user=martin
            Console.WriteLine("Creating listener on https://+:443/" + Program.prefix + "/");
            listener.Prefixes.Add("https://+:443/" + Program.prefix + "/");
            Console.WriteLine("Creating listener on http://+:80/" + Program.prefix + "/");
            listener.Prefixes.Add("http://+:80/" + Program.prefix + "/");
            listener.Start();
            Console.WriteLine("Scheduler stopped.");
        }

        // get web requests
        public void Server()
        {
            try
            {
                while (listener.IsListening) listener.BeginGetContext(ListenerCallback, null).AsyncWaitHandle.WaitOne();
            }
            catch { }
        }

        // new web request, setup a session and as Session class to handle it
        private void ListenerCallback(IAsyncResult ar)
        {
            try
            {
                HttpListenerContext context;
                try
                { context = listener.EndGetContext(ar); }
                catch (HttpListenerException)
                { return; }
                HttpListenerRequest request = context.Request;

                // setup the session
                Session session;
                string sessionKey = null;
                for (int i = 0; i < request.Cookies.Count; i++) if (request.Cookies[i].Name == "session") sessionKey = request.Cookies[i].Value;
                if (sessionKey != null)
                {
                    if (sessions.ContainsKey(sessionKey)) session = sessions[sessionKey];
                    else sessions.Add(sessionKey, session = new Session());
                }
                else
                {
                    TimeSpan diff = DateTime.Now - (new DateTime(1970, 1, 1));
                    sessionKey = Program.GetHashString(Convert.ToString(diff.Milliseconds));
                    sessions.Add(sessionKey, session = new Session());
                    context.Response.AppendCookie(new Cookie("session", sessionKey));
                }
                // check if user not logged in, try to use ntlm (note not turned on as of v0.9)
                if (session.username == null)
                {
                    if (context.User != null && context.User.Identity != null && context.User.Identity.IsAuthenticated)
                    {
                        string tmpname = context.User.Identity.Name.Replace("`", "").Replace("'", "");
                        int r = new SQLiteCommand("UPDATE `users` SET `laston`=CURRENT_TIMESTAMP WHERE `username`='" + context.User.Identity.Name + "'", DB.db).ExecuteNonQuery();
                        if (r == 1) session.username = context.User.Identity.Name.ToLower();
                    }
                }
                // ask Session class to handle a response
                session.ClientRequest(context, DB.db);
            }
            catch { }
        }

        // create simple pie chart picture
        public static void BakePie(string filename, Brush pie, Brush peace, float peaceAngle, int size, int margin, Pen border)
        {
            Rectangle rectPie = new Rectangle(margin, margin, size - margin - margin, size - margin - margin);
            Bitmap bmp = new Bitmap(size, size,PixelFormat.Format32bppArgb);
            Graphics g = Graphics.FromImage(bmp);
            //g.FillRectangle(Brushes.White, new Rectangle(0, 0, size, size));
            g.FillEllipse(pie, rectPie);
            g.FillPie(peace, rectPie, 270, peaceAngle);
            if (border != null) g.DrawEllipse(border, rectPie);
            bmp.Save(Program.path+"deviceonline\\" + filename, ImageFormat.Png);
        }
    }


}
