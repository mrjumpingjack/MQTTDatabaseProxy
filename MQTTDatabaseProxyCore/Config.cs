using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.InteropServices.ComTypes;

namespace MQTTDatabaseProxyCore
{
    public class Config
    {
        public GlobalConfig Globalconfig { get; set; }
        public List<Topic> Topics { get; set; }
        public Config()
        {
            
        }

        public void SetMQTTServerConfig()
        {
            foreach (var Topic in Topics)
            {
                if (Topic.Ip == null) { Topic.Ip = Globalconfig.Ip; }
                if (Topic.Port == 0) { Topic.Port = Globalconfig.Port; }
                if (Topic.User == null) { Topic.User = Globalconfig.User; }
                if (Topic.Password == null) { Topic.Password = Globalconfig.Password; }
            }
        }
    }

    public class Topic
    {
        public string Name { get; set; }
        public TimeOut TimeOut { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public List<string> Ignore { get; set; }
        public string ContentType { get; set; }
        public string Regex { get; set; }
        public List<Mapping> Mappings { get; set; }
        public List<JSONMapping> JSONMappings { get; set; }

        public string DBTable
        {
            get;
            set;
        }
    }




    public class Mapping
    {
        public TimeOut TimeOut { get; set; }
        public string DBTable { get; set; }
        public string Input { get; set; }
        public string Output { get; set; }
        public string Regex { get; set; }
        public List<Extra> Extras { get; set; }
    }

    public class JSONMapping
    {
        public string DBTable { get; set; }
        public TimeOut TimeOut { get; set; }
        public string Input { get; set; }
        public string Output { get; set; }
        public string Regex { get; set; }
        public List<JSONMappingCreteria> JSONMappingCreterias { get; set; }
        public List<Extra> Extras { get; set; }
    }



    public class JSONMappingCreteria
    {
        public string Target { get; set; }
        public string Output { get; set; }
        public string Value { get; set; }
    }

    public class Extra
    {
        public bool Override { get; set; } = false;
        public string Target { get; set; }
        public string Value { get; set; }
    }


    public class GlobalConfig
    {
        public string UserPrefix { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; }
        public string User { get; set; }
        public string Password { get; set; }


        public string DatabaseUri { get; set; }
        public string DatabaseUser { get; set; }
        public string DatabasePassword { get; set; }
        public string DatabaseName { get; set; }
        public LogDatabase LogDatabase { get; set; }

        
    }


    public class LogDatabase
    {
        public string DatabaseUri { get; set; }
        public string DatabaseUser { get; set; }
        public string DatabasePassword { get; set; }
        public string DatabaseName { get; set; }

        public string DBTable { get; set; }
    }



    public static class SystemExtension
    {
        public static T Clone<T>(this T source)
        {
            var serialized = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<T>(serialized);
        }
    }
}