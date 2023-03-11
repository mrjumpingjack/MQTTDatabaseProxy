using System;

namespace MQTTDatabaseProxyCore
{
    public class TimeOut
    {

        public int TimeOutTime { get; set; }
        public DateTime LastTime { get; set; }

        public TimeOut( int timeOutTime)
        {

            TimeOutTime = timeOutTime;
        }
        
        /// <summary>
        /// Returns if the time out is due.
        /// </summary>
        /// <returns></returns>
        public bool IsTime()
        {
            return LastTime.AddSeconds(TimeOutTime) < DateTime.Now;
        }
    }
}