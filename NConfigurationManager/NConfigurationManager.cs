﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;
using System.Configuration;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NConfigurationManager
{
    public static class NConfigurationManager
    {
        private static object _lock = new object();
        private static readonly ReaderWriterLock _readWriteLock = new ReaderWriterLock();
        private static readonly string _rootDirectory;
        private static readonly string _environmentsFile;
        private static readonly FileSystemWatcher _watcher;

        static NConfigurationManager()
        {
            lock (_lock)
            {
                var path = Assembly.GetEntryAssembly().Location;
                while (
                    !Directory.Exists(Path.Combine(path, "nconfig.environments"))
                    && Directory.GetDirectoryRoot(path) != path
                )
                {
                    path = Directory.GetParent(path).FullName;
                }
                if (Directory.GetDirectoryRoot(path) == path)
                    throw new ApplicationException("Could not locate parent NConfig.Environments folder");
                _rootDirectory = Path.Combine(path, "nconfig.environments");
                _environmentsFile = Path.Combine(_rootDirectory, "environments.config");
                if (!File.Exists(_environmentsFile))
                    throw new ApplicationException("Could not locate environments.config file in the NConfig.Environments folder");
                _watcher = new FileSystemWatcher(_rootDirectory, "*.config");

                _watcher.NotifyFilter = NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.Security
                    | NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.FileName
                    | NotifyFilters.Attributes;

                _watcher.Changed += new FileSystemEventHandler(OnChanged);
                _watcher.Created += new FileSystemEventHandler(OnChanged);
                _watcher.Deleted += new FileSystemEventHandler(OnChanged);
                _watcher.Renamed += new RenamedEventHandler(OnRenamed);
                _watcher.EnableRaisingEvents = true;
            }
        }

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            Refresh();
        }
        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            Refresh();
        }
        public static void Initialize()
        {
            Refresh();
        }
        private static void Refresh()
        {
            lock (_lock)
            {
                var environmentConfiguration = GetCurrentEnvironmentConfiguration();
                var activeConfiguration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                try
                {
                    _readWriteLock.AcquireWriterLock(1000);
                    var equalAppSettings = HasEqualAppSettingsAndValues(activeConfiguration, environmentConfiguration);
                    var equalConnectionStrings = HasEqualConnectionStrings(activeConfiguration, environmentConfiguration);
                    if (!equalAppSettings)
                    { 
                        activeConfiguration.AppSettings.Settings.Clear();
                        foreach (KeyValueConfigurationElement a in environmentConfiguration.AppSettings.Settings)
                        {
                            activeConfiguration.AppSettings.Settings.Add(a.Key, a.Value);
                        }
                    }
                    if (!equalConnectionStrings)
                    { 
                        activeConfiguration.ConnectionStrings.ConnectionStrings.Clear();
                        foreach (ConnectionStringSettings s in environmentConfiguration.ConnectionStrings.ConnectionStrings)
                        {
                            activeConfiguration.ConnectionStrings.ConnectionStrings
                                .Add(new ConnectionStringSettings(s.Name, s.ConnectionString, s.ProviderName));
                        }
                    }
                    //we are checking for equality to prevent .Save causing an endless recursion.
                    if (!equalAppSettings || !equalConnectionStrings)
                    { 
                        activeConfiguration.Save(ConfigurationSaveMode.Full);
                        ConfigurationManager.RefreshSection("appSettings");
                        ConfigurationManager.RefreshSection("connectionStrings");
                    }
                }
                catch (Exception e)
                {
                    throw new ApplicationException("Could not copy over configuration", e);
                }
                finally
                {
                    _readWriteLock.ReleaseWriterLock();
                }
            }
        }
        
        public static void Switch(string environment)
        {
            
        }
        private static Configuration GetCurrentEnvironmentConfiguration()
        {
            var environmentsConfiguration = OpenConfiguration(_environmentsFile);
            var settings = environmentsConfiguration.AppSettings.Settings;
            var keys = environmentsConfiguration.AppSettings.Settings.AllKeys;

            var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            var fqdn = string.Format("{0}.{1}", ipProperties.HostName, ipProperties.DomainName);
            var host = ipProperties.HostName;
            var domain = ipProperties.DomainName;
            var ipEntry = Dns.GetHostEntry(host);
            var keyCandidates = new string[] { fqdn, host, domain }.Concat(
                ipEntry.AddressList
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork || a.AddressFamily == AddressFamily.InterNetworkV6)
                .Select(a => a.ToString())
            );

            var environment = settings["default"].Value;
            foreach (var key in keyCandidates)
            {
                if (string.IsNullOrWhiteSpace(key) || !keys.Contains(key.ToString()))
                    continue;
                //TODO log match
                environment = settings[key].Value;
                break;
            }
            if (environment != settings["default"].Value)
            {
                var defaultConfiguration = OpenConfiguration(Path.Combine(_rootDirectory, settings["default"].Value + ".config"));
                var environmentConfiguration = OpenConfiguration(Path.Combine(_rootDirectory, environment + ".config"));

                var equalAppSettings = HasEqualAppSettings(defaultConfiguration, environmentConfiguration);
                if (!equalAppSettings)
                    throw new ApplicationException("Could not load environment: '" + environment + "' it's appSetting misses or has extra keys");

                var equalConnectionStrings = HasEqualAppSettings(defaultConfiguration, environmentConfiguration);
                if (!equalConnectionStrings)
                    throw new ApplicationException("Could not load environment: '" + environment + "' it's connectionStrings misses or has extra connections");
            }
            return OpenConfiguration(Path.Combine(_rootDirectory, environment + ".config"));
        }
        private static bool HasEqualAppSettings(Configuration configuration, Configuration compareConfiguration)
        {
            var configSettings = configuration.AppSettings.Settings.AllKeys;
            var compareSettings = compareConfiguration.AppSettings.Settings.AllKeys;
            var equalAppSettings = configSettings.Count() == compareSettings.Count()
                        && configSettings.Except(compareSettings).Count() == 0;
            return equalAppSettings;
        }
        private static bool HasEqualConnectionStrings(Configuration configuration, Configuration compareConfiguration)
        {
            var configConnections = configuration.ConnectionStrings.ConnectionStrings;
            var compareConnections = compareConfiguration.ConnectionStrings.ConnectionStrings;
            var configConnectionKeys = new List<string>();
            foreach (ConnectionStringSettings k in configConnections)
                configConnectionKeys.Add(k.Name);

            var compareConnectionKeys = new List<string>();
            foreach (ConnectionStringSettings k in compareConnections)
                compareConnectionKeys.Add(k.Name);

            var equalConnectionStrings = configConnections.Count == compareConnections.Count
                && configConnectionKeys.Except(compareConnectionKeys).Count() == 0;

            return equalConnectionStrings;
        }
        private static bool HasEqualAppSettingsAndValues(Configuration configuration, Configuration compareConfiguration)
        {
            if (!HasEqualAppSettings(configuration, compareConfiguration))
                return false;

            var configSettings = configuration.AppSettings.Settings;
            var compareSettings = compareConfiguration.AppSettings.Settings;
            foreach (KeyValueConfigurationElement setting in configSettings)
            {
                if (setting.Value != compareSettings[setting.Key].Value)
                    return false;
            }
            return true;
        }
        private static bool HasEqualConnectionStringsAndValues(Configuration configuration, Configuration compareConfiguration)
        {
            if (!HasEqualConnectionStrings(configuration, compareConfiguration))
                return false;

            var configConnections = configuration.ConnectionStrings.ConnectionStrings;
            var compareConnections = compareConfiguration.ConnectionStrings.ConnectionStrings;
            foreach (ConnectionStringSettings k in configConnections)
            {
                if (k.ConnectionString != compareConnections[k.Name].ConnectionString
                    || k.ProviderName != compareConnections[k.Name].ProviderName)
                    return false;
            }
            return true;
        }


        private static Configuration OpenConfiguration(string path)
        {
            ExeConfigurationFileMap configMap = new ExeConfigurationFileMap();
            configMap.ExeConfigFilename = path;
            var configuration = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
            return configuration;
        }
    }
}