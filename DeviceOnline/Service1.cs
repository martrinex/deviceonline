using System.ServiceProcess;
using System.Threading;

namespace DeviceOnline
{
    /** 
    * @desc DeviceOnline, handles the windows service.
    * -OnStart, service starting tell Program.Start to start web server etc.
    * -OnStop, save db, close web server etc.
    * @author Martin Sykes admin@martrinex.net
    */
    public partial class DeviceOnline : ServiceBase
    {
        public DeviceOnline()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            Program.Start(false);
        }

        protected override void OnStop()
        {
            Program.running = false;
            WebServer.listener.Stop();
            Thread.Sleep(1000);
            DB.db.Close();
            Thread.Sleep(15000); // wait for pinger to stop
        }
    }
}
