using MQTTnet;
using MQTTnet.Client;
using MySqlConnector;
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using MQTTDatabaseProxyCore;
using System.Diagnostics;
using System.IO;

namespace MQTTDatabaseProxyService
{
    public partial class Service1 : ServiceBase
    {
        MQTTDatabaseProxyCore.MQTTDatabaseProxyCore MQTTDatabaseProxyCore = new MQTTDatabaseProxyCore.MQTTDatabaseProxyCore();

        public Service1()
        {
            InitializeComponent();
        }


        string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MQTTDatabaseProxyService");

        private bool startThread = true;

        protected override void OnStart(string[] args)
        {
            string ConfigPath = Path.Combine(AppContext.BaseDirectory, "ProductionConfig.json");
            MQTTDatabaseProxyCore.Log += MQTTDatabaseProxyCore_Log;
            MQTTDatabaseProxyCore.Init(ConfigPath);
        }

        private void MQTTDatabaseProxyCore_Log(object sender, LogEventArgs e)
        {
            if (!Convert.ToBoolean(e.Critical))
            {
                if (!Directory.Exists(LogFilePath))
                {
                    Directory.CreateDirectory(LogFilePath);
                }
                using (StreamWriter sw = new StreamWriter(Path.Combine(LogFilePath, "Log.txt"), true))
                {
                    sw.WriteLine(e.Message);
                }
            }
            else
            {
                this.EventLog.WriteEntry(e.Message, EventLogEntryType.Information);
            }
        }

        protected override void OnStop()
        {
        }

    }
}