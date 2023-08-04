using MQTTnet;
using MQTTnet.Client;
using MySqlConnector;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace MQTTDatabaseProxyCore
{

    public class LogEventArgs : EventArgs
    {
        public string Message { get; set; }
        public bool Critical { get; set; }

        public LogEventArgs()
        {
        }

        public LogEventArgs(string message, bool critical)
        {
            Message = message;
            Critical = critical;
        }
    }

    public class MQTTDatabaseProxyCore
    {
        public Config config;

        public event EventHandler<LogEventArgs> Log;


        private List<IMqttClient> clients = new List<IMqttClient>();

        protected virtual void OnLog(String e, bool critical = false)
        {
            EventHandler<LogEventArgs> handler = Log;
            if (handler != null)
            {
                handler(this, new LogEventArgs(DateTime.Now + ": " + e, critical));
            }
        }

        List<TimeOut> TimeOuts = new List<TimeOut>();

        public void Init(string ConfigPath)
        {
            Thread thread = new Thread(async () =>
            {
                try
                {
                    LoadConfig(ConfigPath);

                    Connect();
                }
                catch (Exception ex)
                {
                    OnLog(ex.Message, true);
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }

        private async void Connect()
        {
            try
            {
                OnLog("Client connecting...");
                if (config != null)
                {
                    if (config.Topics.Count > 0)
                        foreach (var topic in config.Topics)
                        {
                            await SubscribeToTopicAsync(topic.Ip, topic.Port, topic.User, topic.Password, topic.Name, config.Globalconfig.UserPrefix);
                        }
                    else
                        Log(this, new LogEventArgs("No topics found in config file.", true));
                }
                else
                    Log(this, new LogEventArgs("No config found in config file.", true));

            }
            catch (Exception ex)
            {
                OnLog(ex.Message, true);
            }
        }



        private bool LoadConfig(String ConfigPath)
        {
            if (!File.Exists(ConfigPath))
                return false;

            using (StreamReader sr = new StreamReader(ConfigPath))
            {
                config = JsonConvert.DeserializeObject<Config>(sr.ReadToEnd());
                config.SetMQTTServerConfig();
            }

            return true;
        }


        public async Task SubscribeToTopicAsync(string brokerAddress, int port, string username, string password, string topic, string UserPrefix)
        {
            IMqttClient client;
            var factory = new MqttFactory();
            client = factory.CreateMqttClient();

            client.ApplicationMessageReceivedAsync += Client_ApplicationMessageReceivedAsync;
            client.ConnectedAsync += Client_ConnectedAsync;
            client.DisconnectedAsync += Client_DisconnectedAsync;


            var options = new MqttClientOptionsBuilder()
                .WithClientId(UserPrefix + "_" + topic)
                .WithTcpServer(brokerAddress, port)
                .WithCredentials(username, password)
                .Build();

            await client.ConnectAsync(options);

            await client.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .Build());

            OnLog("Client subcribted to: " + topic);

            clients.Add(client);

            InsertDataIntoDatabase(config.Globalconfig.LogDatabase.DatabaseUri, config.Globalconfig.LogDatabase.DatabaseName, config.Globalconfig.LogDatabase.DatabaseUser,
               config.Globalconfig.LogDatabase.DatabasePassword, config.Globalconfig.LogDatabase.DBTable, new Dictionary<string, string>() { { "Name", "Connected" }, { "Value", brokerAddress } });
        }

        private Task Client_ConnectedAsync(MqttClientConnectedEventArgs arg)
        {

            OnLog("Client connected!");
            return Task.CompletedTask;
        }

        private Task Client_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {

            for (int i = clients.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (clients[i].IsConnected)
                    {
                        InsertDataIntoDatabase(config.Globalconfig.LogDatabase.DatabaseUri, config.Globalconfig.LogDatabase.DatabaseName, config.Globalconfig.LogDatabase.DatabaseUser,
                            config.Globalconfig.LogDatabase.DatabasePassword, config.Globalconfig.LogDatabase.DBTable, new Dictionary<string, string>() { { "Name", "Disconnected" }, { "Value", clients[i].Options.ChannelOptions.ToString() + ": " + clients[i].Options.ClientId } });

                        clients[i].ReconnectAsync();
                    }

                }
                catch (Exception ex)
                {
                }
            }

            OnLog("Client disconnected...");

            return Task.CompletedTask;
        }

        private Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
        {
            try
            {
                string TopicName = arg.ApplicationMessage.Topic.ToString();
                string payload = arg.ApplicationMessage.ConvertPayloadToString();


                List<string> IncommingTopicParts = TopicName.Split('/').ToList();
                IncommingTopicParts.Reverse();

                int IncommingIndex = IncommingTopicParts.Count - 1;
                bool PartMatches = true;

                Topic SelectedTopic = null;

                foreach (var topic in config.Topics)
                {
                    PartMatches = true;
                    List<String> CTopicParts = topic.Name.Split('/').ToList();

                    foreach (var CtopicPart in CTopicParts)
                    {
                        if (CtopicPart.Equals(IncommingTopicParts[IncommingIndex]))
                        {
                            IncommingIndex--;
                        }
                        else if (CtopicPart == "#")
                        {
                            IncommingIndex--;
                        }
                        else
                        {
                            PartMatches = false;
                            break;
                        }
                    }

                    if (PartMatches)
                    {
                        SelectedTopic = topic;
                        break;
                    }
                }


                if (SelectedTopic.TimeOut != null)
                    if (!SelectedTopic.TimeOut.IsTime())
                    {
                        OnLog("Blocked because of time out: " + String.Join("/", IncommingTopicParts) + ": " + payload);
                        return Task.CompletedTask;
                    }


                foreach (var ipart in IncommingTopicParts)
                {
                    if (SelectedTopic.Ignore.Contains(ipart))
                        PartMatches = false;
                }

                if (PartMatches == false)
                    return Task.CompletedTask;

                payload = arg.ApplicationMessage.ConvertPayloadToString();
                IncommingTopicParts.Reverse();


                if (SelectedTopic.ContentType == "value")
                {
                    ProcessValueType(payload, IncommingTopicParts, SelectedTopic);
                }
                else if (SelectedTopic.ContentType == "json")
                {
                    ProcessJSONType(payload, IncommingTopicParts, SelectedTopic);
                }


                if (SelectedTopic.TimeOut != null)
                    SelectedTopic.TimeOut.LastTime = DateTime.Now;


            }
            catch (Exception ex)
            {
                OnLog(ex.Message, true);
            }
            return Task.CompletedTask;
        }

        JSONMapping GetMatchingJSONMapping(Topic selectedTopic, List<JSONMapping> JSONmappings, List<string> IncommingTopicParts, string input)
        {
            bool MappinsIsSuitable = true;

            JSONmappings = JSONmappings.OrderBy(m => m.Input.Split('/').Length).ThenBy(m => m.Input.Split('/').Count(sm => sm.Equals('.'))).ToList();

            JSONmappings.Reverse();


            foreach (var JSONmapping in JSONmappings)
            {
                MappinsIsSuitable = true;

                var IParts = JSONmapping.Input.Split('/').ToList();

                if (IParts.Count != IncommingTopicParts.Count)
                    continue;

                for (int i = IncommingTopicParts.Count - 1; i >= 0; i--)
                {
                    if (IParts[i] == "." || IParts[i] == IncommingTopicParts[i])
                    {
                    }
                    else if (IParts[i] == "-")
                    {
                    }
                    else
                    {
                        MappinsIsSuitable = false;
                    }
                }

                if (MappinsIsSuitable)
                    return JSONmapping;
            }

            return null;
        }




        private void ProcessValueType(string payload, List<string> IncommingTopicParts, Topic SelectedTopic)
        {
            var SelectedMapping = GetMatchingMapping(SelectedTopic, SelectedTopic.Mappings, IncommingTopicParts, payload);


            if (SelectedMapping.TimeOut != null)
            {
                if (!SelectedMapping.TimeOut.IsTime())
                {
                    OnLog("Blocked because of time out: " + String.Join("/", IncommingTopicParts) + ": " + payload);
                    return;
                }
            }


            String TableName = SelectedTopic.DBTable;


            if (String.IsNullOrEmpty(SelectedTopic.DBTable))
                TableName = SelectedMapping.DBTable;
            else
                TableName = SelectedTopic.DBTable;





            var IParts = SelectedMapping.Input.Split('/').ToList();
            var OParts = SelectedMapping.Output.Split('/').ToList();


            Dictionary<string, string> Result = new Dictionary<string, string>();


            foreach (var opart in OParts)
            {
                Result.Add(opart, "");
            }

            for (int i = IncommingTopicParts.Count - 1; i >= 0; i--)
            {
                if (IParts[i] == "." || IParts[i].Trim('%') == IncommingTopicParts[i])
                {
                    Result[OParts[i]] = IncommingTopicParts[i];
                }
                else if (IParts[i] == "-")
                {
                    break;
                }
            }

            Regex regex = null;

            if (!String.IsNullOrEmpty(SelectedTopic.Regex))
                regex = new Regex(SelectedTopic.Regex);

            if (!String.IsNullOrEmpty(SelectedMapping.Regex))
                regex = new Regex(SelectedMapping.Regex);

            if (regex != null)
                payload = regex.Match(payload).Groups[0].Value;


            double payloadDouble;
            if (double.TryParse(payload, NumberStyles.Any, CultureInfo.InvariantCulture, out payloadDouble))
            {
                var tmpPayload = payload.ToString().Replace(',', '.');

                Result["Value"] = Convert.ToDouble(tmpPayload, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            }
            else
                Result["Value"] = payload.ToString();


            if (SelectedMapping.Extras != null)
                foreach (var extra in SelectedMapping.Extras)
                {
                    if (Result.ContainsKey(extra.Target))
                    {
                        if (extra.Override)
                        {
                            Result[extra.Target] = extra.Value;
                        }
                    }
                    else
                        Result.Add(extra.Target, extra.Value);
                }




            Result.Remove("-");


            Dictionary<string, string> TResult = new Dictionary<string, string>();

            foreach (var r in Result)
            {
                if (!(r.Key.StartsWith("%") && r.Key.EndsWith("%")))
                {
                    TResult.Add(r.Key, r.Value);
                }
            }

            InsertDataIntoDatabase(TableName, TResult);



            if (SelectedMapping.TimeOut != null)
                SelectedMapping.TimeOut.LastTime = DateTime.Now;
        }



        private void ProcessJSONType(string payload, List<string> IncommingTopicParts, Topic SelectedTopic)
        {
            try
            {
                var SelectedMapping = GetMatchingJSONMapping(SelectedTopic, SelectedTopic.JSONMappings, IncommingTopicParts, payload);


                if (SelectedMapping == null)
                {
                    ProcessValueType(payload, IncommingTopicParts, SelectedTopic);
                    return;
                }

                if (SelectedMapping.TimeOut != null)
                {
                    if (!SelectedMapping.TimeOut.IsTime())
                    {
                        OnLog("Blocked because of time out: " + String.Join("/", IncommingTopicParts) + ": " + payload);
                        return;
                    }
                }

                dynamic JSONPayload = JsonConvert.DeserializeObject(payload);


                Dictionary<string, string> Result = new Dictionary<string, string>();
                String TableName = SelectedTopic.DBTable;


                if (String.IsNullOrEmpty(SelectedTopic.DBTable))
                    TableName = SelectedMapping.DBTable;
                else
                    TableName = SelectedTopic.DBTable;



                var OParts = SelectedMapping.Output.Split('/').ToList();
                var IParts = SelectedMapping.Input.Split('/').ToList();


                foreach (var opart in OParts)
                {
                    Result.Add(opart, "");
                }


                for (int i = IncommingTopicParts.Count - 1; i >= 0; i--)
                {
                    if (IParts[i] == "." || IParts[i] == IncommingTopicParts[i])
                    {
                        Result[OParts[i]] = IncommingTopicParts[i];
                    }
                    else if (IParts[i] == "-")
                    {
                        break;
                    }
                }

                Result.Remove("-");
                Result.Add("Value", null);

                foreach (var criteria in SelectedMapping.JSONMappingCreterias)
                {
                    var vp = criteria.Value.Split('.');
                    dynamic v = JSONPayload;

                    foreach (var vpp in vp)
                    {
                        var r = v[vpp.ToString()];
                        v = r;
                    }

                    Result.Add(criteria.Target, criteria.Output);



                    double payloadDouble;
                    if (double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out payloadDouble))
                    {
                        var tmpPayload = v.ToString().Replace(',', '.');

                        if (Result.ContainsKey("Value"))
                            Result["Value"] = Convert.ToDouble(tmpPayload, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                        else
                            Result.Add("Value", tmpPayload);
                    }
                    else
                    {
                        if (Result.ContainsKey("Value"))
                            Result["Value"] = v.ToString();
                        else
                            Result.Add("Value", v.ToString());
                    }


                    if (SelectedMapping.Extras != null)
                        foreach (var extra in SelectedMapping.Extras)
                        {
                            if (Result.ContainsKey(extra.Target))
                            {
                                if (extra.Override)
                                {
                                    Result[extra.Target] = extra.Value;
                                }
                            }
                            else
                                Result.Add(extra.Target, extra.Value);
                        }

                    InsertDataIntoDatabase(TableName, Result);
                    Result.Remove(criteria.Target);


                    if (SelectedMapping.TimeOut != null)
                        SelectedMapping.TimeOut.LastTime = DateTime.Now;

                }
            }
            catch (Exception ex)
            {
                OnLog("While trying to parse input as JSON: " + ex.Message);
                OnLog("Trying to parse it as one value.");
                ProcessValueType(payload, IncommingTopicParts, SelectedTopic);
            }
        }



        Mapping GetMatchingMapping(Topic selectedTopic, List<Mapping> mappings, List<string> IncommingTopicParts, string input)
        {
            bool MappinsIsSuitable = true;

            mappings = mappings.OrderBy(m => m.Input.Split('/').Length).ToList();

            mappings.Reverse();


            foreach (var JSONmapping in mappings)
            {
                MappinsIsSuitable = true;

                var IParts = JSONmapping.Input.Split('/').ToList();

                if (IParts.Count != IncommingTopicParts.Count)
                    continue;

                for (int i = IncommingTopicParts.Count - 1; i >= 0; i--)
                {
                    if (IParts[i] == "." || IParts[i] == IncommingTopicParts[i])
                    {
                    }
                    else if (IParts[i] == "-")
                    {
                    }
                    else
                    {
                        MappinsIsSuitable = false;
                    }
                }

                if (MappinsIsSuitable)
                    return JSONmapping;
            }

            return null;
        }



        public void InsertDataIntoDatabase(string Table, Dictionary<string, string> data)
        {
            InsertDataIntoDatabase(config.Globalconfig.DatabaseUri, config.Globalconfig.DatabaseName, config.Globalconfig.DatabaseUser, config.Globalconfig.DatabasePassword, Table, data);
        }


        public void InsertDataIntoDatabase(string databaseUri, string dataBase, string username, string password, string table, Dictionary<string, string> data)
        {

            if (!CheckDatabaseExsits(databaseUri, dataBase, username, password))
            {
                if (!CreateDatabase(databaseUri, dataBase, username, password))
                {
                    OnLog("Database " + dataBase + " could not be created");
                    return;
                }
            }

            if (!CheckTableExsists(databaseUri, dataBase, username, password, table))
            {
                if (!CreateTable(databaseUri, dataBase, username, password, table))
                {
                    OnLog("Table " + table + " could not be created in database " + dataBase);
                    return;
                }
            }

            foreach (var col in data)
            {
                if (!CheckColumnExists(databaseUri, dataBase, username, password, table, col.Key))
                {
                    if (!AddColumnToTable(databaseUri, dataBase, username, password, table, col.Key))
                    {
                        OnLog("Column " + col + " could not be created in table " + table + " in database " + dataBase);
                        return;
                    }
                }
            }





            try
            {
                string connectionString =
                    "Server=" + databaseUri +
                    ";Database=" + dataBase +
                    ";user=" + username +
                    ";password=" + password + ";";

                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {

                    string query = "INSERT INTO " + table + " (";

                    foreach (var dp in data)
                    {
                        query += "`" + dp.Key + "`, ";
                    }
                    query = query.Trim(' ').Trim(',');

                    query += ") VALUES (";

                    foreach (var dp in data)
                    {
                        query += "'" + dp.Value + "', ";
                    }

                    query = query.Trim(' ').Trim(',');

                    query += ");";

                    using (MySqlCommand command = new MySqlCommand(query, connection))
                    {
                        connection.Open();
                        command.ExecuteNonQuery();

                        OnLog("Success on: " + query);
                    }
                }

            }
            catch (Exception ex)
            {
                OnLog(ex.Message, true);
            }
        }



        public bool CheckDatabaseExsits(string databaseUri, string dataBase, string username, string password)
        {
            try
            {
                string connectionString =
                    "Server=" + databaseUri +
                    ";Database=" + dataBase +
                    ";user=" + username +
                    ";password=" + password + ";";

                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    string query = "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = '" + dataBase + "'";


                    using (MySqlCommand command = new MySqlCommand(query, connection))
                    {
                        connection.Open();

                        using (MySqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.HasRows)
                                return true;
                            else
                                return false;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public bool CreateDatabase(string databaseUri, string dataBase, string username, string password)
        {

            try
            {
                string connectionString =
                    "Server=" + databaseUri +
                    ";user=" + username +
                    ";password=" + password + ";";

                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    String query = "CREATE DATABASE IF NOT EXISTS "+ dataBase+";";


                    using (MySqlCommand command = new MySqlCommand(query, connection))
                    {
                        connection.Open();
                        command.ExecuteNonQuery();

                        return true;
                    }
                }

            }
            catch (Exception ex)
            {
                return false;
            }


        }






        public bool CheckTableExsists(string databaseUri, string dataBase, string username, string password, string table)
        {
            try
            {
                string connectionString =
                    "Server=" + databaseUri +
                    ";Database=" + dataBase +
                    ";user=" + username +
                    ";password=" + password + ";";

                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    string query = "SHOW TABLES LIKE '" + table + "';";
                    
                    using (MySqlCommand command = new MySqlCommand(query, connection))
                    {
                        connection.Open();
                        using (MySqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.HasRows)
                                return true;
                            else
                                return false;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public bool CreateTable(string databaseUri, string dataBase, string username, string password, string table)
        {

            try
            {
                string connectionString =
                    "Server=" + databaseUri +
                    ";Database=" + dataBase +
                    ";user=" + username +
                    ";password=" + password + ";";

                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    String query = "CREATE TABLE " + table + " (id int NOT NULL AUTO_INCREMENT, `creation_time` DATETIME DEFAULT CURRENT_TIMESTAMP, PRIMARY KEY (id));";


                    using (MySqlCommand command = new MySqlCommand(query, connection))
                    {
                        connection.Open();
                        command.ExecuteNonQuery();

                        return true;
                    }
                }

            }
            catch (Exception ex)
            {
                return false;
            }


        }



        public bool CheckColumnExists(string databaseUri, string dataBase, string username, string password, string table, string ColName)
        {
            string connectionString =
                  "Server=" + databaseUri +
                  ";Database=" + dataBase +
                  ";user=" + username +
                  ";password=" + password + ";";

            using (MySqlConnection connection = new MySqlConnection(connectionString))
            {
                using (MySqlCommand command = new MySqlCommand("SHOW COLUMNS FROM `" + table + "` LIKE '" + ColName + "';", connection))
                {
                    connection.Open();
                    using (MySqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.HasRows)
                            return true;
                        else
                            return false;
                    }
                }
            }
        }

        public bool AddColumnToTable(string databaseUri, string dataBase, string username, string password, string table, string ColumnName)
        {
            try
            {
                string connectionString =
                  "Server=" + databaseUri +
                  ";Database=" + dataBase +
                  ";user=" + username +
                  ";password=" + password + ";";

                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {

                    using (MySqlCommand command = new MySqlCommand("ALTER TABLE " + table + " ADD `" + ColumnName + "` text(100);", connection))
                    {
                        connection.Open();
                        command.ExecuteNonQuery();

                    }
                }
                return true;
            }
            catch (Exception)
            {
                OnLog("Failed to create column " + ColumnName + " into " + dataBase);
                return false;
            }
        }


    }
}