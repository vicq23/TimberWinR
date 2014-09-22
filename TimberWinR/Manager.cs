﻿using System.IO;
using System.Net.Sockets;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TimberWinR.Inputs;
using TimberWinR.Outputs;
using System.Threading;

namespace TimberWinR
{
    /// <summary>
    /// The Manager class for TimberWinR
    /// </summary>
    public class Manager
    {
        public Configuration Config { get; set; }
        public List<OutputSender> Outputs { get; set; }
        public List<TcpInputListener> Tcps { get; set; }
        public List<InputListener> Listeners { get; set;  }
        public DateTime StartedOn { get; set; }
        public string JsonConfig { get; set; }
        public string LogfileDir { get; set; }

        public int NumConnections {
            get { return numConnections; }
        }

        public int NumMessages
        {
            get { return numMessages; }
        }

        private static int numConnections;
        private static int numMessages;

     
        public void Shutdown()
        {
            LogManager.GetCurrentClassLogger().Info("Shutting Down");

            foreach (InputListener listener in Listeners)
                listener.Shutdown();
        }


        public void IncrementMessageCount(int count = 1)
        {            
            Interlocked.Add(ref numMessages, count);
        }
    
        public Manager(string jsonConfigFile, string logLevel, string logfileDir, CancellationToken cancelToken)
        {
            StartedOn = DateTime.UtcNow;

            var vfi = new FileInfo(jsonConfigFile);

            JsonConfig = vfi.FullName;
            LogfileDir = logfileDir;


            numMessages = 0;
            numConnections = 0;

            Outputs = new List<OutputSender>();           
            Listeners = new List<InputListener>();
           
            var loggingConfiguration = new LoggingConfiguration();

            // Create our default targets
            var coloredConsoleTarget = new ColoredConsoleTarget();

            Target fileTarget = CreateDefaultFileTarget(logfileDir);

            loggingConfiguration.AddTarget("Console", coloredConsoleTarget);
            loggingConfiguration.AddTarget("DailyFile", fileTarget);

            // The LogLevel.Trace means has to be at least Trace to show up on console
            loggingConfiguration.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, coloredConsoleTarget));
            // LogLevel.Debug means has to be at least Debug to show up in logfile
            loggingConfiguration.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, fileTarget));
          
            LogManager.Configuration = loggingConfiguration;
            LogManager.EnableLogging();

            LogManager.GlobalThreshold = LogLevel.FromString(logLevel);


            // Is it a directory?
            if (Directory.Exists(jsonConfigFile))
            {
                DirectoryInfo di = new DirectoryInfo(jsonConfigFile);
                LogManager.GetCurrentClassLogger().Info("Initialized, Reading Configurations From {0}", di.FullName);
                Config = Configuration.FromDirectory(jsonConfigFile);              
            }
            else
            {
                var fi = new FileInfo(jsonConfigFile);

                LogManager.GetCurrentClassLogger().Info("Initialized, Reading Configurations From File: {0}", fi.FullName);
               
                if (!fi.Exists)
                    throw new FileNotFoundException("Missing config file", jsonConfigFile);

                LogManager.GetCurrentClassLogger().Info("Initialized, Reading Config: {0}", fi.FullName);
                Config = Configuration.FromFile(jsonConfigFile);
            }
         
            LogManager.GetCurrentClassLogger().Info("Log Directory {0}", logfileDir);
            LogManager.GetCurrentClassLogger().Info("Logging Level: {0}", LogManager.GlobalThreshold);        
            
            // Read the Configuration file
            if (Config != null)
            {
                if (Config.RedisOutputs != null)
                {
                    foreach (var ro in Config.RedisOutputs)
                    {
                        var redis = new RedisOutput(this, ro, cancelToken);
                        Outputs.Add(redis);
                    }

                }
                if (Config.ElasticsearchOutputs != null)
                {
                    foreach (var ro in Config.ElasticsearchOutputs)
                    {
                        var els = new ElasticsearchOutput(this, ro, cancelToken);
                        Outputs.Add(els);
                    }
                }
                if (Config.StdoutOutputs != null)
                {
                    foreach (var ro in Config.StdoutOutputs)
                    {
                        var stdout = new StdoutOutput(this, ro, cancelToken);
                        Outputs.Add(stdout);
                    }
                }

                foreach (Parser.IISW3CLog iisw3cConfig in Config.IISW3C)
                {
                    var elistner = new IISW3CInputListener(iisw3cConfig, cancelToken);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                foreach (Parser.W3CLog iisw3cConfig in Config.W3C)
                {
                    var elistner = new W3CInputListener(iisw3cConfig, cancelToken);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                foreach (Parser.WindowsEvent eventConfig in Config.Events)
                {
                    var elistner = new WindowsEvtInputListener(eventConfig, cancelToken);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                foreach (var logConfig in Config.Logs)
                {
                    var elistner = new LogsListener(logConfig, cancelToken);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }

                foreach (var tcp in Config.Tcps)
                {
                    var elistner = new TcpInputListener(cancelToken, tcp.Port);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }


                foreach (var tcp in Config.Stdins)
                {
                    var elistner = new StdinListener(cancelToken);
                    Listeners.Add(elistner);
                    foreach (var output in Outputs)
                        output.Connect(elistner);
                }
            }
        }

        /// <summary>
        /// Creates the default <see cref="FileTarget"/>.
        /// </summary>
        /// <param name="logPath"></param>
        /// <returns>
        /// The NLog file target used in the default logging configuration.
        /// </returns>
        public static FileTarget CreateDefaultFileTarget(string logPath)
        {
            return new FileTarget
            {
                ArchiveEvery = FileArchivePeriod.None,
                ArchiveAboveSize = 5 * 1024 * 1024,
                MaxArchiveFiles = 5,
                BufferSize = 10,
                FileName = Path.Combine(logPath, "TimberWinR", "TimberWinR.log"),
                ArchiveFileName = Path.Combine(logPath, "TimberWinR_log-{#######}.log"),
            };
        }

    }
}
