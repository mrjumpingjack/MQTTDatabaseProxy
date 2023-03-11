using MQTTnet;
using MQTTnet.Client;
using MySqlConnector;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace MQTTDatabaseProxyCore
{
    public class MQTTDatabaseProxyCore
    {
        private IMqttClient client;
        public Config config;

        public event EventHandler<string[]> Log;

        protected virtual void OnLog(String e, bool critical = false)
        {
            EventHandler<string[]> handler = Log;
            if (handler != null)
            {
                handler(this, new string[] {e,critical.ToString()});
            }
        }


        public void Init(string ConfigPath)
        {
            Thread thread = new Thread(async () =>
            {
                try
                {
                    LoadConfig(ConfigPath);

                    Connect();
                    client.DisconnectedAsync += Client_DisconnectedAsync;

                }
                catch (Exception ex)
                {
                   OnLog(ex.Message,true);
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }

        private async void Connect()
        {
            OnLog("Client connecting...");
            foreach (var topic in config.Topics)
            {
                await SubscribeToTopicAsync(topic.Ip, topic.Port, topic.User, topic.Password, topic.Name, config.Globalconfig.UserPrefix);
            }
        }

        private Task Client_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {
            OnLog("Client disconnected...");
            Connect();
            return Task.CompletedTask;
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
            var factory = new MqttFactory();
            client = factory.CreateMqttClient();

            client.ApplicationMessageReceivedAsync += Client_ApplicationMessageReceivedAsync;
            client.ConnectedAsync += Client_ConnectedAsync;

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
        }

        private Task Client_ConnectedAsync(MqttClientConnectedEventArgs arg)
        {
            OnLog("Client connected!");
            return Task.CompletedTask;
        }

        private Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
        {
            try
            {
                string TopicName = arg.ApplicationMessage.Topic.ToString();
                string payload = arg.ApplicationMessage.ConvertPayloadToString();

                //if (!TopicName.Contains("SENSOR"))
                //    return Task.CompletedTask;



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
                {
                    if (!SelectedTopic.TimeOut.IsTime())
                    {
                        OnLog("Blocked because of time out: " + String.Join("/", IncommingTopicParts) + ": " + payload);
                        return Task.CompletedTask;
                    }
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
                    try
                    {
                        Dictionary<string, string> Result = new Dictionary<string, string>();

                        var SelectedMapping = GetMatchingJSONMapping(SelectedTopic, SelectedTopic.JSONMappings, IncommingTopicParts, payload);

                        dynamic JSONPayload = JsonConvert.DeserializeObject(payload);

                        if (String.IsNullOrEmpty(SelectedTopic.DBTable))
                            SelectedTopic.DBTable = SelectedMapping.DBTable;



                        if (SelectedMapping.TimeOut != null)
                        {
                            if (!SelectedMapping.TimeOut.IsTime())
                            {
                                OnLog("Blocked because of time out: " + String.Join("/", IncommingTopicParts) + ": " + payload);
                                return Task.CompletedTask;
                            }
                            else
                                SelectedMapping.TimeOut.LastTime = DateTime.Now;
                        }


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

                            InsertDataIntoData(SelectedTopic, Result);
                            Result.Remove(criteria.Target);


                        }
                    }
                    catch (Exception ex)
                    {
                        OnLog("While trying to parse input as JSON: " + ex.Message);
                        OnLog("Trying to parse it as one value.");
                        ProcessValueType(payload, IncommingTopicParts, SelectedTopic);
                    }
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


            if (String.IsNullOrEmpty(SelectedTopic.DBTable))
                SelectedTopic.DBTable = SelectedMapping.DBTable;



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

            InsertDataIntoData(SelectedTopic, TResult);

            if (SelectedMapping.TimeOut != null)
                SelectedMapping.TimeOut.LastTime = DateTime.Now;
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



        public void InsertDataIntoData(Topic topic, Dictionary<string, string> data)
        {

            string query = "";

            try
            {
                string connectionString =
                    "Server=" + config.Globalconfig.DatabaseUri +
                    ";Database=" + config.Globalconfig.DatabaseName +
                    ";user=" + config.Globalconfig.DatabaseUser +
                    ";password=" + config.Globalconfig.DatabasePassword + ";";

                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {

                    query = "INSERT INTO " + topic.DBTable + " (";

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
                        //connection.Close();

                        OnLog("Success on: " + query);
                    }
                }

            }
            catch (Exception ex)
            {
                OnLog(ex.Message, true);
                OnLog(ex.Message, true);
            }
        }

    }
}