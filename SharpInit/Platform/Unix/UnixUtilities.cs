using Mono.Unix.Native;

namespace SharpInit.Platform.Unix
{
    public static class UnixUtilities
    {
        public static int WriteToFd(int fd, string text) =>
            WriteToFd(fd, System.Text.Encoding.UTF8.GetBytes(text));
        public static int WriteToFd(int fd, byte[] bytes)
        {
            var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, buffer, bytes.Length);

            try 
            {
                return (int)Syscall.write(fd, buffer, (ulong)bytes.Length);
            }
            catch
            {
                return -1;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }
    }
}