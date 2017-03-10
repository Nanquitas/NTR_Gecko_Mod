// Originally from https://github.com/Chadderz121/tcp-gecko-dotnet/
using System;
using System.Net.Sockets;
using System.IO;

namespace Gecko
{
    public class TCPConnection
    {
        private TcpClient _client;
        private NetworkStream _stream;
        
        public string Host { get; private set; }
        public int Port { get; private set; }

        public TCPConnection(string host, int port)
        {
            Host = host;
            Port = port;
            _client = null;
            _stream = null;
        }

        public void Connect()
        {
            try
            {
                Close();
            }
            catch (Exception)
            {
                // ignored
            }
            _client = new TcpClient {NoDelay = true};
            IAsyncResult ar = _client.BeginConnect(Host, Port, null, null);
            System.Threading.WaitHandle wh = ar.AsyncWaitHandle;
            try
            {
                if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5), false))
                {
                    _client.Close();
                    throw new IOException("Connection timoeut.", new TimeoutException());
                }

                _client.EndConnect(ar);
            }
            finally
            {
                wh.Close();
            }
            _stream = _client.GetStream();
            _stream.ReadTimeout = 10000;
            _stream.WriteTimeout = 10000;
        }

        public void Close()
        {
            try
            {
                if (_client == null)
                {
                    throw new IOException("Not connected.", new NullReferenceException());
                }
                _client.Close();

            }
            catch (Exception)
            {
                // ignored
            }
            finally
            {
                _client = null;
            }
        }

        public void Purge()
        {
            if (_stream == null)
            {
                throw new IOException("Not connected.", new NullReferenceException());
            }
            _stream.Flush();
        }

        public void Read(Byte[] buffer, UInt32 nobytes, ref UInt32 bytesRead)
        {
            try
            {
                int offset = 0;
                if (_stream == null)
                {
                    throw new IOException("Not connected.", new NullReferenceException());
                }
                bytesRead = 0;
                while (nobytes > 0)
                {
                    int read = _stream.Read(buffer, offset, (int)nobytes);
                    if (read >= 0)
                    {
                        bytesRead += (uint)read;
                        offset += read;
                        nobytes -= (uint)read;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                throw new IOException("Connection closed.", e);
            }
        }

        public void Write(Byte[] buffer, Int32 nobytes, ref UInt32 bytesWritten)
        {
            try
            {
                if (_stream == null)
                {
                    throw new IOException("Not connected.", new NullReferenceException());
                }
                _stream.Write(buffer, 0, nobytes);
                if (nobytes >= 0)
                    bytesWritten = (uint)nobytes;
                else
                    bytesWritten = 0;
                _stream.Flush();
            }
            catch (ObjectDisposedException e)
            {
                throw new IOException("Connection closed.", e);
            }
        }
    }
}
