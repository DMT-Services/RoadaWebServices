using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace DMTServices
{
    public static class BitConverterExtension
    {
        public static UInt64 BCDToUInt64(this byte[] b, int startOf, int len)
        {
            UInt64 result = 0;
            int p = startOf;
            int i = 0;
            len *= 2;
            int nib;
            while (i < len)
            {
                result *= 10;
                result += (UInt64)(b[startOf] >> 4);
                if (++i < len)
                {
                    nib = (b[startOf++] & 0xf);
                    if (nib != 0xf) // end of data
                    {
                        result *= 10;
                        result += (UInt64)nib;
                    }
                }
                i++;
            }

            return (result);

        }

        /// <summary>
        /// Convert Big Endian to Little Endian
        /// </summary>
        /// <param name="b">byte array to convert</param>
        /// <param name="pos">pos in byte array to start</param>
        /// <param name="len"># of bytes to convert</param>
        /// <returns></returns>
        public static byte[] Flip(this byte[] b, int pos, int len)
        {
            byte[] Result = new byte[len];
            for (int i = 0; i < len; i++)
                Result[i] = b[(len + pos) - (i + 1)];
            return Result;
        }

        public static byte HexToNib(byte b)
        {
            if (b > '9')
                b -= 7;
            return (byte)(b & 0xf);
        }

        public static UInt16 HexToUInt16(this byte[] b, int pos)
        {
            UInt16 Result = 0;
            byte b2;
            for (int i = 0; i < 4; i++)
            {
                Result <<= 4;
                b2 = b[pos++];
                if (b2 > '9')
                    b2 -= 7;
                Result |= (UInt16)(b2 & 0xf);
            }
            return Result;
        }

        public static UInt16 BEToUInt16(this byte[] b, int pos)
        {
            UInt16 Result = 0;
            Result = BitConverter.ToUInt16(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            return Result;
        }

        public static Int16 BEToInt16(this byte[] b, int pos)
        {
            Int16 Result = 0;
            Result = BitConverter.ToInt16(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            return Result;
        }

        public static UInt64 BEToUInt64(this byte[] b, int pos)
        {
            UInt64 Result = 0;
            Result = BitConverter.ToUInt64(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            return Result;
        }

        public static Int64 BEToInt64(this byte[] b, int pos)
        {
            Int64 Result = 0;
            Result = BitConverter.ToInt64(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            return Result;
        }

        public static UInt32 BEToUInt32(this byte[] b, int pos)
        {
            UInt32 Result = 0;
            Result = BitConverter.ToUInt32(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            return Result;
        }

        public static Int32 BEToInt32(this byte[] b, int pos)
        {
            Int32 Result = 0;
            Result = BitConverter.ToInt32(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            return Result;
        }

        public static UInt16 BEToUInt16(this byte[] b, ref int pos)
        {
            UInt16 Result = 0;
            Result = BitConverter.ToUInt16(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            pos += Marshal.SizeOf(Result);
            return Result;
        }

        public static Int16 BEToInt16(this byte[] b, ref int pos)
        {
            Int16 Result = 0;
            Result = BitConverter.ToInt16(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            pos += Marshal.SizeOf(Result);
            return Result;
        }

        public static UInt64 BEToUInt64(this byte[] b, ref int pos)
        {
            UInt64 Result = 0;
            Result = BitConverter.ToUInt64(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            pos += Marshal.SizeOf(Result);
            return Result;
        }

        public static Int64 BEToInt64(this byte[] b, ref int pos)
        {
            Int64 Result = 0;
            Result = BitConverter.ToInt64(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            pos += Marshal.SizeOf(Result);
            return Result;
        }

        public static UInt32 BEToUInt32(this byte[] b, ref int pos)
        {
            UInt32 Result = 0;
            Result = BitConverter.ToUInt32(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            pos += Marshal.SizeOf(Result);
            return Result;
        }

        public static Int32 BEToInt32(this byte[] b, ref int pos)
        {
            Int32 Result = 0;
            Result = BitConverter.ToInt32(b.Flip(pos, Marshal.SizeOf(Result)), 0);
            pos += Marshal.SizeOf(Result);
            return Result;
        }


        public static UInt16 ToUInt16(this byte[] b, int pos)
        {
            UInt16 Result = 0;
            Result = BitConverter.ToUInt16(b, pos);
            return Result;
        }

        public static Int16 ToInt16(this byte[] b, int pos)
        {
            Int16 Result = 0;
            Result = BitConverter.ToInt16(b, pos);
            return Result;
        }

        public static UInt32 ToUInt32(this byte[] b, int pos)
        {
            UInt32 Result = 0;
            Result = BitConverter.ToUInt32(b, pos);
            return Result;
        }

        public static Int32 ToInt32(this byte[] b, int pos)
        {
            Int32 Result = 0;
            Result = BitConverter.ToInt32(b, pos);
            return Result;
        }

        public static void ValueToBE(this byte[] b, int pos, UInt64 value)
        {
            Array.Copy(BitConverter.GetBytes(value).Flip(0, Marshal.SizeOf(value)), 0, b, pos, Marshal.SizeOf(value));
        }

        public static void ValueToBE(this byte[] b, int pos, UInt32 value)
        {
            Array.Copy(BitConverter.GetBytes(value).Flip(0, Marshal.SizeOf(value)), 0, b, pos, Marshal.SizeOf(value));
        }

        public static void ValueToBE(this byte[] b, int pos, UInt16 value)
        {
            Array.Copy(BitConverter.GetBytes(value).Flip(0, Marshal.SizeOf(value)), 0, b, pos, Marshal.SizeOf(value));
        }

        public static void ValueToBE(this byte[] b, int pos, Int16 value)
        {
            Array.Copy(BitConverter.GetBytes(value).Flip(0, Marshal.SizeOf(value)), 0, b, pos, Marshal.SizeOf(value));
        }

        public static void FloatToBytes(this byte[] b, ref int pos, float value)
        {
            Array.Copy(BitConverter.GetBytes(value), 0, b, pos, Marshal.SizeOf(value));
            pos += Marshal.SizeOf(value);
        }

        public static void IntToBytes(this byte[] b, ref int pos, Int32 value)
        {
            Array.Copy(BitConverter.GetBytes(value), 0, b, pos, Marshal.SizeOf(value));
            pos += Marshal.SizeOf(value);
        }

        public static void IntToBytes(this byte[] b, ref int pos, UInt32 value)
        {
            Array.Copy(BitConverter.GetBytes(value), 0, b, pos, Marshal.SizeOf(value));
            pos += Marshal.SizeOf(value);
        }

        public static void IntToBytes(this byte[] b, ref int pos, UInt16 value)
        {
            Array.Copy(BitConverter.GetBytes(value), 0, b, pos, Marshal.SizeOf(value));
            pos += Marshal.SizeOf(value);
        }

        public static bool FlattenDate(this byte[] dest, DateTime rdate, ref int p, bool addDate)
        {
            if (addDate)
            {
                dest[p++] = (byte)(rdate.Year - 2000);
                dest[p++] = (byte)rdate.Month;
                dest[p++] = (byte)rdate.Day;
            }
            dest[p++] = (byte)rdate.Hour;
            dest[p++] = (byte)rdate.Minute;
            dest[p++] = (byte)rdate.Second;

            if (rdate == DateTime.MinValue)
                return false;
            else
                return true;
        }

        public static DateTime CalAmpToDateTime(this byte[] b, int pos)
        {
            DateTime Result = DateTime.MinValue;
            long ticks = new DateTime(1970, 1, 1).Ticks;      // need ticks from Jan 1/1970 as per CalAmp spec
            // calamp values are big endian, so must flip it and convert
            UInt32 calTicks = b.BEToUInt32(pos);
            // since one C# tick is 100 nanosecond, and CalAmp is in seconds, we must multiply the CalAmp value X 10 million
            ticks += (long)calTicks * 10000000L;
            return new DateTime(ticks);
        }
    }
}
