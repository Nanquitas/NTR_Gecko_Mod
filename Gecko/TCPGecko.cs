// Originally from https://github.com/Chadderz121/tcp-gecko-dotnet/
#define DIRECT

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Gecko
{
    public class ByteSwap
    {
        public static UInt16 Swap(UInt16 input)
        {
            if (BitConverter.IsLittleEndian)
                return (UInt16) (
                    ((0xFF00 & input) >> 8) |
                    ((0x00FF & input) << 8));
            return input;
        }

        public static UInt32 Swap(UInt32 input)
        {
            if (BitConverter.IsLittleEndian)
                return ((0xFF000000 & input) >> 24) |
                       ((0x00FF0000 & input) >> 8) |
                       ((0x0000FF00 & input) << 8) |
                       ((0x000000FF & input) << 24);
            return input;
        }

        public static UInt64 Swap(UInt64 input)
        {
            if (BitConverter.IsLittleEndian)
                return ((0xFF00000000000000 & input) >> 56) |
                       ((0x00FF000000000000 & input) >> 40) |
                       ((0x0000FF0000000000 & input) >> 24) |
                       ((0x000000FF00000000 & input) >> 8) |
                       ((0x00000000FF000000 & input) << 8) |
                       ((0x0000000000FF0000 & input) << 24) |
                       ((0x000000000000FF00 & input) << 40) |
                       ((0x00000000000000FF & input) << 56);
            return input;
        }
    }

    public class Dump
    {
        private int _fileNumber;

        public Byte[] Mem;

        public Dump(UInt32 theStartAddress, UInt32 theEndAddress)
        {
            Construct(theStartAddress, theEndAddress, 0);
        }

        public Dump(UInt32 theStartAddress, UInt32 theEndAddress, int theFileNumber)
        {
            Construct(theStartAddress, theEndAddress, theFileNumber);
        }

        public UInt32 StartAddress { get; private set; }

        public UInt32 EndAddress { get; private set; }

        public UInt32 ReadCompletedAddress { get; set; }

        private void Construct(UInt32 theStartAddress, UInt32 theEndAddress, int theFileNumber)
        {
            StartAddress = theStartAddress;
            EndAddress = theEndAddress;
            ReadCompletedAddress = theStartAddress;
            Mem = new Byte[EndAddress - StartAddress];
            _fileNumber = theFileNumber;
        }

        public UInt32 ReadAddress32(UInt32 addressToRead)
        {
            if (addressToRead < StartAddress) return 0;
            if (addressToRead > EndAddress - 4) return 0;

            Byte[] buffer = new Byte[4];
            Buffer.BlockCopy(Mem, Index(addressToRead), buffer, 0, 4);

            //Read buffer
            return BitConverter.ToUInt32(buffer, 0);
        }

        private int Index(UInt32 addressToRead)
        {
            return (int) (addressToRead - StartAddress);
        }

        public UInt32 ReadAddress(UInt32 addressToRead, int numBytes)
        {
            if (addressToRead < StartAddress) return 0;
            if (addressToRead > EndAddress - numBytes) return 0;

            Byte[] buffer = new Byte[4];
            Buffer.BlockCopy(Mem, Index(addressToRead), buffer, 0, numBytes);

            //Read buffer
            switch (numBytes)
            {
                case 4:
                    return BitConverter.ToUInt32(buffer, 0);

                case 2:
                    return BitConverter.ToUInt16(buffer, 0);

                default:
                    return buffer[0];
            }
        }

        public void WriteStreamToDisk()
        {
            string myDirectory = Environment.CurrentDirectory + @"\dumps\";

            if (!Directory.Exists(myDirectory))
                Directory.CreateDirectory(myDirectory);

            string myFile = myDirectory + "dump" + _fileNumber + ".dmp";

            WriteStreamToDisk(myFile);
        }

        public void WriteStreamToDisk(string filepath)
        {
            FileStream file = new FileStream(filepath, FileMode.Create);

            file.Write(Mem, 0, (int) (EndAddress - StartAddress));
            file.Close();
            file.Dispose();
        }
    }

    public enum ETCPErrorCode
    {
        FTDIQueryError,
        NoFTDIDevicesFound,
        NoTCPGeckoFound,
        FTDIResetError,
        FTDIPurgeRxError,
        FTDIPurgeTxError,
        FTDITimeoutSetError,
        FTDITransferSetError,
        FTDICommandSendError,
        FTDIReadDataError,
        FTDIWriteDataError,
        FTDIInvalidAddress,
        FTDIInvalidReply,
        TooManyRetries,
        RegStreamSizeInvalid,
        CheatStreamSizeInvalid
    }

    public enum FTDICommand
    {
        CmdResultError,
        CmdFatalError,
        CmdOk
    }

    public enum WiiStatus
    {
        Running,
        Paused,
        Breakpoint,
        Loader,
        Unknown
    }

    public delegate void GeckoProgress(
        UInt32 address, UInt32 currentchunk, UInt32 allchunks, UInt32 transferred, UInt32 length, bool okay, bool dump);

    public class ETCPGeckoException : Exception
    {
        public ETCPGeckoException(ETCPErrorCode code)
        {
            ErrorCode = code;
        }

        public ETCPGeckoException(ETCPErrorCode code, string message)
            : base(message)
        {
            ErrorCode = code;
        }

        public ETCPGeckoException(ETCPErrorCode code, string message, Exception inner)
            : base(message, inner)
        {
            ErrorCode = code;
        }

        public ETCPErrorCode ErrorCode { get; }
    }

    public class TCPGecko
    {
        private static volatile TCPGecko _instance;
        private readonly object _networkInUse = new object();

        private TCPConnection _tcp;

        public TCPGecko(string host, int port)
        {
            _tcp = new TCPConnection(host, port);
            Connected = false;
            PChunkUpdate = null;
            _instance = this;
        }

        public static TCPGecko Instance => _instance;

        public bool Connected { get; private set; }

        public bool CancelDump { get; set; }

        public string Host
        {
            get { return _tcp.Host; }
            set
            {
                if (!Connected)
                    _tcp = new TCPConnection(value, _tcp.Port);
            }
        }

        public int Port
        {
            get { return _tcp.Port; }
            set
            {
                if (!Connected)
                    _tcp = new TCPConnection(_tcp.Host, value);
            }
        }

        private event GeckoProgress PChunkUpdate;

        public event GeckoProgress ChunkUpdate
        {
            add { PChunkUpdate += value; }
            remove { PChunkUpdate -= value; }
        }

        ~TCPGecko()
        {
            if (Connected)
                Disconnect();
        }

        public bool Connect()
        {
            if (Connected)
                Disconnect();

            Connected = false;

            //Open TCP Gecko
            try
            {
                _tcp.Connect();
            }
            catch (IOException)
            {
                // Don't disconnect if there's nothing connected
                Disconnect();
                throw new ETCPGeckoException(ETCPErrorCode.NoTCPGeckoFound);
            }

            //Initialise TCP Gecko
            Thread.Sleep(150);
            Connected = true;
            return true;
        }

        public void Disconnect()
        {
            Connected = false;
            _tcp.Close();
        }

        protected FTDICommand GeckoRead(Byte[] recbyte, UInt32 nobytes)
        {
            lock (_networkInUse)
            {
                UInt32 bytesRead = 0;

                try
                {
                    _tcp.Read(recbyte, nobytes, ref bytesRead);
                }
                catch (IOException)
                {
                    Disconnect();
                    return FTDICommand.CmdFatalError; // fatal error
                }
                if (bytesRead != nobytes)
                    return FTDICommand.CmdResultError; // lost bytes in transmission

                return FTDICommand.CmdOk;
            }
        }

        protected FTDICommand GeckoWrite(Byte[] sendbyte, Int32 nobytes)
        {
            lock (_networkInUse)
            {
                UInt32 bytesWritten = 0;

                try
                {
                    _tcp.Write(sendbyte, nobytes, ref bytesWritten);
                }
                catch (IOException)
                {
                    Disconnect();
                    return FTDICommand.CmdFatalError; // fatal error
                }
                if (bytesWritten != nobytes)
                    return FTDICommand.CmdResultError; // lost bytes in transmission

                return FTDICommand.CmdOk;
            }
        }

        protected FTDICommand GeckoWrite(UInt32[] array, uint size = 0)
        {
            int max = size == 0 ? array.Length : (int)size;
            int nobytes = max*4;
            byte[] sendbyte = new byte[nobytes];
            
            for (int i = 0; i < max; i++)
            {
                sendbyte[i*4] = BitConverter.GetBytes(array[i])[0];
                sendbyte[i*4 + 1] = BitConverter.GetBytes(array[i])[1];
                sendbyte[i*4 + 2] = BitConverter.GetBytes(array[i])[2];
                sendbyte[i*4 + 3] = BitConverter.GetBytes(array[i])[3];
            }

            lock (_networkInUse)
            {
                UInt32 bytesWritten = 0;

                try
                {
                    _tcp.Write(sendbyte, nobytes, ref bytesWritten);
                }
                catch (IOException)
                {
                    Disconnect();
                    return FTDICommand.CmdFatalError; // fatal error
                }
                if (bytesWritten != nobytes)
                    return FTDICommand.CmdResultError; // lost bytes in transmission

                return FTDICommand.CmdOk;
            }
        }

        //Send update on a running process to the parent class
        protected void SendUpdate(UInt32 address, UInt32 currentchunk, UInt32 allchunks, UInt32 transferred,
            UInt32 length, bool okay, bool dump)
        {
            PChunkUpdate?.Invoke(address, currentchunk, allchunks, transferred, length, okay, dump);
        }

        public void Dump(Dump dump)
        {
            //Stream[] tempStream = { dump.dumpStream, dump.getOutputStream() };
            //Stream[] tempStream = { dump.dumpStream };
            //Dump(dump.startAddress, dump.endAddress, tempStream);
            //dump.getOutputStream().Dispose();
            //dump.WriteStreamToDisk();
            Dump(dump.StartAddress, dump.EndAddress, dump);
        }

        public void Dump(UInt32 startdump, UInt32 enddump, Stream saveStream)
        {
            Stream[] tempStream = {saveStream};
            Dump(startdump, enddump, tempStream);
        }

        private bool CheckDumpStatus()
        {
            lock (_networkInUse)
            {
                byte[] response = new byte[1];

                if (GeckoRead(response, 1) != FTDICommand.CmdOk)
                {
                    //Major fail, give it up
                    GeckoWrite(BitConverter.GetBytes(GCFAIL), 1);
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIReadDataError);
                }

                if (response[0] == GCFAIL)
                    return false;
                return true;
            }
        }

        public void Dump(UInt32 startdump, UInt32 enddump, Stream[] saveStream)
        {
            lock (_networkInUse)
            {
                //How many bytes of data have to be transferred
                int memlength = (int) (enddump - startdump);


                // Send the read memory command to client
                if (GeckoWrite(BitConverter.GetBytes(cmd_readmem), 1) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                //Now let's send the dump information
                UInt32[] geckoMemRange = { startdump, enddump };
                if (GeckoWrite(geckoMemRange) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);


                // Reset cancel flag
                bool done = false;
                CancelDump = false;

                Byte[] buffer = new Byte[packetsize]; //read buffer

                uint transfered = 0;
                uint chunkCount = (uint) (memlength/packetsize);

                if ((uint) (memlength%packetsize) > 0)
                    chunkCount++;

                uint chunk = 0;
                int mlength = memlength;
                while ((memlength > 0) && !done)
                {
                    //No output yet availible
                    SendUpdate(startdump + transfered, chunk, chunkCount, transfered, (uint) mlength, true, true);

                    // Check dump status
                    if (!CheckDumpStatus())
                        throw new ETCPGeckoException(ETCPErrorCode.FTDIInvalidAddress);

                    //Set buffer
                    Byte[] response = new Byte[1];
                    if (GeckoRead(response, 1) != FTDICommand.CmdOk)
                    {
                        //Major fail, give it up
                        GeckoWrite(BitConverter.GetBytes(GCFAIL), 1);
                        throw new ETCPGeckoException(ETCPErrorCode.FTDIReadDataError);
                    }

                    Byte reply = response[0];

                    if (reply == BlockZero)
                    {
                        // Send that everything is correct
                        GeckoWrite(BitConverter.GetBytes(GCACK), 1);

                        uint length = (uint) memlength < packetsize ? (uint) memlength : packetsize;

                        // Create the zero filled zone
                        for (int i = 0; i < length; i++)
                            buffer[i] = 0;

                        // Write32 received package to output stream
                        foreach (Stream stream in saveStream)
                            stream.Write(buffer, 0, (Int32) length);

                        memlength -= (int) length;
                        chunk++;
                        transfered += length;
                    }
                    else
                    {
                        // Get the length to read
                        if (GeckoRead(buffer, 4) != FTDICommand.CmdOk)
                            throw new ETCPGeckoException(ETCPErrorCode.FTDIReadDataError);

                        UInt32 length = BitConverter.ToUInt32(buffer, 0);

                        // Read data
                        if (GeckoRead(buffer, length) != FTDICommand.CmdOk)
                        {
                            GeckoWrite(BitConverter.GetBytes(GCFAIL), 1);
                            throw new ETCPGeckoException(ETCPErrorCode.FTDIReadDataError);
                        }

                        // Send that data is correctly received
                        GeckoWrite(BitConverter.GetBytes(GCACK), 1);

                        memlength -= (int) length;
                        transfered += length;
                        chunk++;

                        // Write32 received package to output stream
                        foreach (Stream stream in saveStream)
                            stream.Write(buffer, 0, (Int32) length);

                        if (CancelDump)
                        {
                            // User requested a cancel
                            GeckoWrite(BitConverter.GetBytes(GCFAIL), 1);
                            done = true;
                        }
                    }
                    SendUpdate(startdump + transfered, chunk, chunkCount, transfered, (uint) mlength, true, true);
                }
            }
        }


        public void Dump(UInt32 startdump, uint enddump, Dump memdump)
        {
            lock (_networkInUse)
            {
                //How many bytes of data have to be transferred
                int memlength = (int) (enddump - startdump);

                // Send the read memory command to client
                if (GeckoWrite(BitConverter.GetBytes(cmd_readmem), 1) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                //Now let's send the dump information
                UInt32[] geckoMemRange = {startdump, enddump};

                if (GeckoWrite(geckoMemRange) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);


                // Reset cancel flag
                bool done = false;
                CancelDump = false;

                Byte[] buffer = new Byte[packetsize]; //read buffer

                uint transfered = 0;
                uint chunkCount = (uint) (memlength/packetsize);

                if ((uint) (memlength%packetsize) > 0)
                    chunkCount++;

                uint chunk = 0;
                int mlength = memlength;
                while ((memlength > 0) && !done)
                {
                    //No output yet availible
                    SendUpdate(startdump + transfered, chunk, chunkCount, transfered, (uint) mlength, true, true);

                    // Check dump status
                    if (!CheckDumpStatus())
                        throw new ETCPGeckoException(ETCPErrorCode.FTDIInvalidAddress);


                    //Set buffer
                    Byte[] response = new Byte[1];
                    if (GeckoRead(response, 1) != FTDICommand.CmdOk)
                    {
                        //Major fail, give it up
                        GeckoWrite(BitConverter.GetBytes(GCFAIL), 1);
                        throw new ETCPGeckoException(ETCPErrorCode.FTDIReadDataError);
                    }

                    Byte reply = response[0];

                    if (reply == BlockZero)
                    {
                        // Send that everything is correct
                        GeckoWrite(BitConverter.GetBytes(GCACK), 1);

                        uint length = (uint) memlength < packetsize ? (uint) memlength : packetsize;
                        // Create the zero filled zone
                        for (int i = 0; i < length; i++)
                            buffer[i] = 0;

                        memlength -= (int) length;
                        chunk++;

                        Buffer.BlockCopy(buffer, 0, memdump.Mem,
                            (int) transfered, (int) length);

                        transfered += length;
                        memdump.ReadCompletedAddress = startdump + transfered;
                    }
                    else
                    {
                        // Get the length to read
                        if (GeckoRead(buffer, 4) != FTDICommand.CmdOk)
                            throw new ETCPGeckoException(ETCPErrorCode.FTDIReadDataError);

                        UInt32 length = BitConverter.ToUInt32(buffer, 0);

                        // Read data
                        if (GeckoRead(buffer, length) != FTDICommand.CmdOk)
                        {
                            GeckoWrite(BitConverter.GetBytes(GCFAIL), 1);
                            throw new ETCPGeckoException(ETCPErrorCode.FTDIReadDataError);
                        }

                        // Send that data is correctly received
                        GeckoWrite(BitConverter.GetBytes(GCACK), 1);

                        memlength -= (int) length;
                        chunk++;

                        Buffer.BlockCopy(buffer, 0, memdump.Mem,
                            (int) transfered, (int) length);

                        transfered += length;
                        memdump.ReadCompletedAddress = startdump + transfered;

                        if (CancelDump)
                        {
                            // User requested a cancel
                            GeckoWrite(BitConverter.GetBytes(GCFAIL), 1);
                            done = true;
                        }
                    }
                    SendUpdate(startdump + transfered, chunk, chunkCount, transfered, (uint) mlength, true, true);
                }
            }
        }

        public void Upload(UInt32 startupload, UInt32 endupload, Stream sendStream)
        {
            if (endupload < startupload)
                throw new ETCPGeckoException(ETCPErrorCode.FTDIInvalidAddress, "End address can't be lower than start address.");

            lock (_networkInUse)
            {
                // How many bytes of data have to be transferred
                uint memlength = endupload - startupload;

                // How many chunks do I need to split this data into
                // How big ist the last chunk
                uint chunkcount = memlength/uplpacketsize;

                if (memlength%uplpacketsize > 0)
                    chunkcount++;

                UInt32[] geckoMemRange = {startupload, endupload};

                // Send command
                if (GeckoWrite(BitConverter.GetBytes(cmd_upload), 1) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                // Now let's send the upload information
                if (GeckoWrite(geckoMemRange) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                byte[] buffer = new byte[uplpacketsize];
                uint transfered = 0;
                int restlength = (int) memlength;
                uint chunk = 0;

                while (restlength > 0)
                {
                    // Update progress
                    SendUpdate(startupload + transfered, chunk, chunkcount, transfered, memlength, true, false);

                    uint length = restlength > uplpacketsize ? uplpacketsize : (uint) restlength;

                    // Read buffer from stream
                    sendStream.Read(buffer, 0, (int) length);

                    if (GeckoWrite(buffer, (int) length) != FTDICommand.CmdOk)
                    {
                        throw new ETCPGeckoException(ETCPErrorCode.FTDIWriteDataError);
                    }
                    chunk++;
                    transfered += length;
                    restlength -= (int) length;
                }

                // Check the good ending of the upload
                byte[] response = new byte[1];

                if (GeckoRead(response, 1) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIReadDataError);

                if (response[0] != GCACK)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIInvalidReply);

                // Update with finished stage
                SendUpdate(startupload + transfered, chunk, chunkcount, transfered, memlength, true, false);
            }
        }

        public bool Reconnect()
        {
            Disconnect();
            try
            {
                return Connect();
            }
            catch
            {
                return false;
            }
        }

        //Allows sending a basic one byte command to the 3DS
        public FTDICommand RawCommand(Byte id)
        {
            return GeckoWrite(BitConverter.GetBytes(id), 1);
        }

        // Sends a GCFAIL to the game.. in case the Gecko handler hangs.. sendfail might solve it!
        public void SendFail()
        {
            //Only needs to send a cmd_unfreeze to Wii
            //Ignores the reply, send this command multiple times!
            RawCommand(GCFAIL);
        }

        // Returns the console status
        public WiiStatus Status()
        {
            lock (_networkInUse)
            {
                Thread.Sleep(100);

                //Send status command
                if (RawCommand(cmd_status) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                //Read status
                Byte[] buffer = new Byte[1];
                if (GeckoRead(buffer, 1) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIReadDataError);

                //analyse reply
                switch (buffer[0])
                {
                    case 0:
                        return WiiStatus.Running;
                    case 1:
                        return WiiStatus.Paused;
                    case 2:
                        return WiiStatus.Breakpoint;
                    case 3:
                        return WiiStatus.Loader;
                    default:
                        return WiiStatus.Unknown;
                }
            }
        }

        public UInt32 VersionRequest()
        {
            lock (_networkInUse)
            {
                if (RawCommand(cmd_version) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                Byte[] buffer = new Byte[4];

                if (GeckoRead(buffer, 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                return BitConverter.ToUInt32(buffer, 0);
            }
        }

        public UInt32 OsVersionRequest()
        {
            lock (_networkInUse)
            {
                if (RawCommand(cmd_os_version) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                Byte[] buffer = new Byte[4];

                if (GeckoRead(buffer, 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                return BitConverter.ToUInt32(buffer, 0);
            }
        }

        public UInt32 KernVersionRequest()
        {
            lock (_networkInUse)
            {
                if (RawCommand(cmd_kern_version) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                Byte[] buffer = new Byte[4];

                if (GeckoRead(buffer, 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                return BitConverter.ToUInt32(buffer, 0);
            }
        }

        public UInt32 TitleTypeRequest()
        {
            lock (_networkInUse)
            {
                if (RawCommand(cmd_title_type) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                Byte[] buffer = new Byte[4];

                if (GeckoRead(buffer, 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                return BitConverter.ToUInt32(buffer, 0);
            }
        }

        public UInt32 TitleIDRequest()
        {
            lock (_networkInUse)
            {
                if (RawCommand(cmd_title_id) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                Byte[] buffer = new Byte[4];

                if (GeckoRead(buffer, 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                return BitConverter.ToUInt32(buffer, 0);
            }
        }

        public UInt32 GamePIDRequest()
        {
            lock (_networkInUse)
            {
                if (RawCommand(cmd_game_pid) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                Byte[] buffer = new Byte[4];

                if (GeckoRead(buffer, 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                return BitConverter.ToUInt32(buffer, 0);
            }
        }

        public string GameNameRequest()
        {
            lock (_networkInUse)
            {
                if (RawCommand(cmd_game_name) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                // Is there any log to fetch ?
                byte[] buffer = new byte[8];

                if (GeckoRead(buffer, 8) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                return Encoding.UTF8.GetString(buffer);
            }
        }

        public UInt32[] MemoryRegionRequest()
        {
            lock (_networkInUse)
            {
                // Fetch how many regions we have
                if (RawCommand(cmd_list_region) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                Byte[] buffer = new Byte[1];

                if (GeckoRead(buffer, 1) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                UInt32 count = buffer[0]; //ByteSwap.Swap(BitConverter.ToUInt32(buffer, 0));

                if (count <= 0)
                    return null;

                // Result format: UInt32[3] = {start_addr, size, type}
                UInt32[] regions = new UInt32[count*3];

                uint size = count*3*4;
                // Allocate buffer to receive data
                buffer = new byte[size];

                if (GeckoRead(buffer, size) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                for (int i = 0, j = 0; i < buffer.Length; i += 4, j++)
                    regions[j] = BitConverter.ToUInt32(buffer, i);

                return regions;
            }
        }

        public void PatchWireless()
        {
            lock (_networkInUse)
            {
                if (RawCommand(cmd_patch_wireless) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);
            }
        }

        public string LogRequest()
        {
            lock (_networkInUse)
            {
                string log = "";

                if (RawCommand(cmd_fetch_log) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                // Is there any log to fetch ?
                byte[] buffer = new byte[4];

                if (GeckoRead(buffer, 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                UInt32 logLen = BitConverter.ToUInt32(buffer, 0);
                if (logLen > 0)
                {
                    buffer = new byte[logLen];

                    if (GeckoRead(buffer, logLen) != FTDICommand.CmdOk)
                        throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                    log = Encoding.UTF8.GetString(buffer);
                }

                return log;
            }
        }

        public UInt32 Peek(UInt32 address)
        {
            lock (_networkInUse)
            {
                //address will be alligned to 4
                UInt32 paddress = address & 0xFFFFFFFC;

                //Create a memory stream for the actual dump
                MemoryStream stream = new MemoryStream();

                //make sure to not send data to the output
                GeckoProgress oldUpdate = PChunkUpdate;
                PChunkUpdate = null;

                try
                {
                    //dump data
                    Dump(paddress, paddress + 4, stream);

                    //go to beginning
                    stream.Seek(0, SeekOrigin.Begin);
                    Byte[] buffer = new Byte[4];
                    stream.Read(buffer, 0, 4);

                    //Read buffer
                   return BitConverter.ToUInt32(buffer, 0);
                }
                finally
                {
                    PChunkUpdate = oldUpdate;

                    //make sure the Stream is properly closed
                    stream.Close();
                }
            }
        }

        #region base constants

        private const UInt32 packetsize = 0x1000; // 0x400;
        private const UInt32 uplpacketsize = 0x1000; //0x400;

        private const Byte cmd_poke08 = 0x01;
        private const Byte cmd_poke16 = 0x02;
        private const Byte cmd_pokemem = 0x03;
        private const Byte cmd_readmem = 0x04;
        private const Byte cmd_pause = 0x06;
        private const Byte cmd_unfreeze = 0x07;
        private const Byte cmd_breakpoint = 0x09;
        private const Byte cmd_writekern = 0x0b;
        private const Byte cmd_readkern = 0x0c;
        private const Byte cmd_breakpointx = 0x10;
        private const Byte cmd_sendregs = 0x2F;
        private const Byte cmd_getregs = 0x30;
        private const Byte cmd_cancelbp = 0x38;
        private const Byte cmd_sendcheats = 0x40;
        private const Byte cmd_upload = 0x41;
        private const Byte cmd_hook = 0x42;
        private const Byte cmd_hookpause = 0x43;
        private const Byte cmd_step = 0x44;
        private const Byte cmd_status = 0x50;
        private const Byte cmd_title_type = 0x51;
        private const Byte cmd_title_id = 0x52;
        private const Byte cmd_game_pid = 0x53;
        private const Byte cmd_game_name = 0x54;
        private const Byte cmd_patch_wireless = 0x55;
        private const Byte cmd_add_cheat = 0x56;
        private const Byte cmd_delete_cheat = 0x57;
        private const Byte cmd_enable_cheat = 0x58;
        private const Byte cmd_disable_cheat = 0x59;
        private const Byte cmd_list_cheats = 0x60;
        private const Byte cmd_rpc = 0x70;
        private const Byte cmd_nbreakpoint = 0x89;
        private const Byte cmd_version = 0x99;
        private const Byte cmd_os_version = 0x9A;
        private const Byte cmd_kern_version = 0x9B;
        private const Byte cmd_list_region = 0x9C;
        private const Byte cmd_fetch_log = 0x9D;

        private const Byte GCBPHit = 0x11;
        private const Byte GCACK = 0xAA;
        private const Byte GCRETRY = 0xBB;
        private const Byte GCFAIL = 0xCC;
        private const Byte GCDONE = 0xFF;

        private const Byte BlockZero = 0xB0;
        private const Byte BlockNonZero = 0xBD;

        private const Byte GCWiiVer = 0x80;
        private const Byte GCNgcVer = 0x81;
        private const Byte GCWiiUVer = 0x82;
        private const Byte N3DSVersion = 0x83;

        private static readonly Byte[] GCAllowedVersions = {GCWiiUVer};

        private const Byte BPExecute = 0x03;
        private const Byte BPRead = 0x05;
        private const Byte BPWrite = 0x06;
        private const Byte BPReadWrite = 0x07;

        #endregion

        #region ReadWrite

        public void Write32(uint address, uint value)
        {
            lock (_networkInUse)
            {
                // 4 align address
                address &= 0xFFFFFFFC;

                //Send poke
                if (RawCommand(cmd_pokemem) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                //write value
                UInt32[] pokeVal = { address, value };
                if (GeckoWrite(pokeVal) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);
            }
        }

        public void Write16(uint address, ushort value)
        {
            lock (_networkInUse)
            {
                // Lower address
                address &= 0xFFFFFFFE;

                // Send poke16
                if (RawCommand(cmd_poke16) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                // Write value
                UInt32[] pokeVal = { address, value };
                if (GeckoWrite(pokeVal) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);
            }
        }

        public void Write8(uint address, byte value)
        {
            lock (_networkInUse)
            {
                //Send poke08
                if (RawCommand(cmd_poke08) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                //write value
                UInt32[] pokeVal = { address, value };

                if (GeckoWrite(pokeVal) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);
            }
        }

        public uint Read32(uint address)
        {
            lock (_networkInUse)
            {
                // 4 aligned address
                address &= ~0x3u;

                // Create a memory stream for the actual dump
                MemoryStream stream = new MemoryStream();

                try
                {
                    // Dump data
                    Dump(address, address + 4, stream);

                    // Go to start of the stream
                    stream.Seek(0, SeekOrigin.Begin);

                    byte[] buffer = new byte[4];
                    stream.Read(buffer, 0, 4);

                    //Read buffer
                    return BitConverter.ToUInt32(buffer, 0);
                }
                finally
                {
                    // Make sure the Stream is properly closed
                    stream.Close();
                }
            }
        }
        public uint Read16(uint address)
        {
            lock (_networkInUse)
            {
                int offset = (int)address & 0x3;

                // 4 aligned address
                address &= ~0x3u;

                // Create a memory stream for the actual dump
                MemoryStream stream = new MemoryStream();

                try
                {
                    // Dump data
                    Dump(address, address + 4, stream);

                    // Go to start of the stream
                    stream.Seek(0, SeekOrigin.Begin);

                    byte[] buffer = new byte[4];
                    stream.Read(buffer, 0, 4);

                    //Read buffer
                    return BitConverter.ToUInt16(buffer, offset);
                }
                finally
                {
                    // Make sure the Stream is properly closed
                    stream.Close();
                }
            }
        }
        public uint Read8(uint address)
        {
            lock (_networkInUse)
            {
                int offset = (int)address & 0x3;

                // 4 aligned address
                address &= ~0x3u;

                //Create a memory stream for the actual dump
                MemoryStream stream = new MemoryStream();

                try
                {
                    // Dump data
                    Dump(address, address + 4, stream);

                    // Go to start of the stream
                    stream.Seek(0, SeekOrigin.Begin);

                    byte[] buffer = new byte[4];
                    stream.Read(buffer, 0, 4);

                    //Read buffer
                    return buffer[offset];
                }
                finally
                {
                    // Make sure the Stream is properly closed
                    stream.Close();
                }
            }
        }

        #endregion
        #region CHEATS
        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        public int SendCheat(string cheat)
        {
            if (String.IsNullOrEmpty(cheat) || String.IsNullOrWhiteSpace(cheat))
                return (-1);

            uint[] c = new uint[0x100];
            uint size = 0;
            uint index = 0;
            string name = "";
            foreach (string line in cheat.Split(Environment.NewLine.ToCharArray()))
            {
                if (string.IsNullOrEmpty(line))
                    continue;
                if (line.Contains("["))
                {
                    name = line.Replace('[', ' ').Replace(']', ' ').Trim();
                    continue;
                }
                string[] commands = line.Split(' ');

                byte[] ar = StringToByteArray(commands[0]).Reverse().ToArray();
                uint u = BitConverter.ToUInt32(ar, 0);
                c[index++] = BitConverter.ToUInt32(StringToByteArray(commands[0]).Reverse().ToArray(), 0);
                c[index++] = BitConverter.ToUInt32(StringToByteArray(commands[1]).Reverse().ToArray(), 0);
                size += 8;
            }

            lock (_networkInUse)
            {
                // Send command byte
                if (RawCommand(cmd_add_cheat) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                // Send code's size
                if (GeckoWrite(BitConverter.GetBytes(size), 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIWriteDataError);

                // Send code
                if (GeckoWrite(c, (size / 4)) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIWriteDataError);

                byte[] bname = Encoding.UTF8.GetBytes(name);

                // Send name's size
                size = (uint) bname.Length;
                byte[] buffer = BitConverter.GetBytes(size);

                if (GeckoWrite(buffer, 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIWriteDataError);

                // Send name
                if (size > 0)
                {
                    if (GeckoWrite(bname, (int)size) != FTDICommand.CmdOk)
                        throw new ETCPGeckoException(ETCPErrorCode.FTDIWriteDataError);
                }

                // Receive id of the cheat
                buffer = new byte[4];
                if (GeckoRead(buffer, 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIReadDataError);

                return BitConverter.ToInt32(buffer, 0);

            }
        }

        public void RemoveCheat(int id)
        {
            lock (_networkInUse)
            {
                // Send command
                if (RawCommand(cmd_delete_cheat) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                // Send index
                if (GeckoWrite(BitConverter.GetBytes(id), 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIWriteDataError);
            }
        }

        public void EnableCheat(int id)
        {
            lock (_networkInUse)
            {
                // Send command
                if (RawCommand(cmd_enable_cheat) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                // Send index
                if (GeckoWrite(BitConverter.GetBytes(id), 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIWriteDataError);
            }
        }

        public void DisableCheat(int id)
        {
            lock (_networkInUse)
            {
                // Send command
                if (RawCommand(cmd_disable_cheat) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);

                // Send index
                if (GeckoWrite(BitConverter.GetBytes(id), 4) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDIWriteDataError);
            }
        }

        public void ListCheats()
        {
            lock (_networkInUse)
            {
                // Send command
                if (RawCommand(cmd_list_cheats) != FTDICommand.CmdOk)
                    throw new ETCPGeckoException(ETCPErrorCode.FTDICommandSendError);
            }
        }
        #endregion

    }
}