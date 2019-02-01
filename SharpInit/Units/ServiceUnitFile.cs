﻿using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    public class ServiceUnitFile : ExecUnitFile
    {
        [UnitProperty("Service/Type", UnitPropertyType.Enum, ServiceType.Simple, typeof(ServiceType))]
        public ServiceType ServiceType { get; set; }

        [UnitProperty("Service/RemainAfterExit", UnitPropertyType.Bool, false)]
        public bool RemainAfterExit { get; set; }

        [UnitProperty("Service/GuessMainPID", UnitPropertyType.Bool, false)]
        public bool GuessMainPID { get; set; }

        [UnitProperty("Service/PIDFile", UnitPropertyType.String)]
        public string PIDFile { get; set; }

        [UnitProperty("Service/BusName", UnitPropertyType.String)]
        public string BusName { get; set; }

        [UnitProperty("Service/ExecStart", UnitPropertyType.StringList)]
        public List<string> ExecStart { get; set; }

        public ServiceUnitFile() { }
        public ServiceUnitFile(string path) => UnitParser.Parse<ServiceUnitFile>(path);
    }

    public enum ServiceType
    {
        Simple, // only one supported so far
        Exec,
        Forking,
        Oneshot,
        Dbus,
        Notify,
        Idle
    }
}