using System;
using System.Xml.Serialization;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DMTServices;

namespace DMTServices
{
    /// <summary>
    /// this class encapsulates a single packet that
    /// is either sent or received by a UDP socket
    /// </summary>
    [Serializable]
    public class TCPPacketBuffer
    {
        public int Index;       // location in List
        public bool Taken = false;
        public bool Closed = false;
        public bool DupCheckFlag = true;
        public bool Busy = false;
        public bool TimedOut = false;
        // this is for timing processes!
        public DateTime lastDate = DateTime.MinValue;
        // size of the buffer
        public const int BUFFER_SIZE = 65536;        // this size must be enough to hold the DCD messages!!
        public string IPAddress = null;

        [XmlIgnore]
        public byte[] Buffer = new byte[BUFFER_SIZE];

        [XmlIgnore]
        public byte[] hdrBuffer = new Byte[5];      // for header info (length etc...)

        // Data is now dynamically resized on the fly
        public byte[] Data;
        [XmlIgnore]
        public byte[] SendBuf = new byte[BUFFER_SIZE];
        public int DataLength = 0;

        //buffer for network i/o
        public int DataSize = 0;

        public int DevId = 0;
        public byte BlkCnt = 0;     // the last block count
        public byte Vers;

        //data size to be received by the server
        //flag that indicates whether prefix was received
        public bool DataSizeReceived = false;
        public int DataSizeState = 0;
        //place where data is stored
        [XmlIgnore] 
        public SocketAsyncEventArgs ReceiveEventArgs = new SocketAsyncEventArgs();
        [XmlIgnore]
        public SocketAsyncEventArgs SendEventArgs = new SocketAsyncEventArgs();
        [XmlIgnore]
        public Socket Client;
        [XmlIgnore]
        public FileStream fsObject = null;
        
        // the buffer itself
//        public byte[] Data;

        // length of data to transmit
//        public int DataLength;

        // the (IP)Endpoint of the remote host
//        public EndPoint RemoteEndPoint;

        public TCPPacketBuffer()
        {
            this.Buffer = new byte[BUFFER_SIZE];
            this.Data = new byte[0];
        }

        public TCPPacketBuffer(EventHandler<SocketAsyncEventArgs> asyncHandler)
        {
            this.Buffer = new byte[BUFFER_SIZE];
            this.Data = new byte[0];
            ReceiveEventArgs.Completed += asyncHandler;
            SendEventArgs.Completed += asyncHandler;
        }

        public int WriteCmd(char cmd, byte[] data, ushort len)
        {
            // cmd in buf[0]
            this.SendBuf[0] = (byte)cmd;
            // length goes in buf[1]
            this.SendBuf[1] = (byte)(len & 0xff);        // this is better than a BitConverter, no heap fragmentation
            this.SendBuf[2] = (byte)(len >> 8);
            // block counter
            this.SendBuf[3] = this.BlkCnt;
            if (len > 0)
                Array.Copy(data, 0, this.SendBuf, 4, len);
            this.SendEventArgs.SetBuffer(this.SendBuf, 0, len + 4);
            this.Client.SendAsync(this.SendEventArgs);
            return len + 4;
        }

        public int WriteCmd(char cmd)
        {
            return WriteCmd(cmd, null, 0);
        }

        public void ResetBusy()
        {
            // reset for next message
            DataLength = 0;
            DataSizeReceived = false;
            DataSizeState = 0;
            DataSize = 0;
            Busy = false;     // we're done processing!
        }
    }

}
