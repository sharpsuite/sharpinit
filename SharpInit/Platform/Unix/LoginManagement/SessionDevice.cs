namespace SharpInit.Platform.Unix.LoginManagement
{
    public class SessionDevice
    {
        public uint DeviceNumber { get; set; }
        public string SysPath { get; set; }
        public int DeviceFd { get; set; }
        public SessionDeviceType DeviceType { get; set; }
        public UdevDevice Device { get; set; }
        
        public Session Session { get; set; }
        
        public SessionDevice(UdevDevice dev)
        {
            Device = dev;
            SysPath = Device.SysPath;

            if (Device.Subsystem == "drm")
                DeviceType = SessionDeviceType.DRM;
            if (Device.Subsystem == "input")
                DeviceType = SessionDeviceType.Evdev;
            
        }
    }
}