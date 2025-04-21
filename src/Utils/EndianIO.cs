using System;
using System.IO;
using System.Text;

namespace AssetRipper.IO.Endian
{
    /// <summary>
    /// Enum to specify the endianness type
    /// </summary>
    public enum EndianType
    {
        LittleEndian,
        BigEndian
    }

    /// <summary>
    /// A BinaryReader that handles endianness for different platforms
    /// </summary>
    public class EndianReader : BinaryReader
    {
        private readonly bool m_bigEndian;

        public EndianReader(Stream stream, bool bigEndian) : base(stream)
        {
            m_bigEndian = bigEndian;
        }

        public EndianReader(Stream stream, EndianType endianType) : base(stream)
        {
            m_bigEndian = endianType == EndianType.BigEndian;
        }

        public EndianReader(Stream stream, Encoding encoding, bool bigEndian) : base(stream, encoding)
        {
            m_bigEndian = bigEndian;
        }

        public EndianReader(Stream stream, Encoding encoding, bool leaveOpen, bool bigEndian) : base(stream, encoding, leaveOpen)
        {
            m_bigEndian = bigEndian;
        }

        public override short ReadInt16()
        {
            short value = base.ReadInt16();
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            return value;
        }

        public override int ReadInt32()
        {
            int value = base.ReadInt32();
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            return value;
        }

        public override long ReadInt64()
        {
            long value = base.ReadInt64();
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            return value;
        }

        public override ushort ReadUInt16()
        {
            ushort value = base.ReadUInt16();
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            return value;
        }

        public override uint ReadUInt32()
        {
            uint value = base.ReadUInt32();
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            return value;
        }

        public override ulong ReadUInt64()
        {
            ulong value = base.ReadUInt64();
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            return value;
        }

        public override float ReadSingle()
        {
            if (m_bigEndian)
            {
                byte[] bytes = base.ReadBytes(4);
                Array.Reverse(bytes);
                return BitConverter.ToSingle(bytes, 0);
            }
            return base.ReadSingle();
        }

        public override double ReadDouble()
        {
            if (m_bigEndian)
            {
                byte[] bytes = base.ReadBytes(8);
                Array.Reverse(bytes);
                return BitConverter.ToDouble(bytes, 0);
            }
            return base.ReadDouble();
        }

        private static short SwapBytes(short value)
        {
            return (short)(((ushort)value >> 8) | ((ushort)value << 8));
        }

        private static int SwapBytes(int value)
        {
            return (int)(((value & 0x000000FF) << 24) |
                   ((value & 0x0000FF00) << 8) |
                   ((value & 0x00FF0000) >> 8) |
                   ((value & 0xFF000000) >> 24));
        }

        private static long SwapBytes(long value)
        {
            ulong uValue = (ulong)value;
            return (long)(
                    ((uValue & 0x00000000000000FF) << 56) |
                    ((uValue & 0x000000000000FF00) << 40) |
                    ((uValue & 0x0000000000FF0000) << 24) |
                    ((uValue & 0x00000000FF000000) << 8) |
                    ((uValue & 0x000000FF00000000) >> 8) |
                    ((uValue & 0x0000FF0000000000) >> 24) |
                    ((uValue & 0x00FF000000000000) >> 40) |
                    ((uValue & 0xFF00000000000000) >> 56)
                );
        }

        private static ushort SwapBytes(ushort value)
        {
            return (ushort)((value >> 8) | (value << 8));
        }

        private static uint SwapBytes(uint value)
        {
            return ((value & 0x000000FF) << 24) |
                   ((value & 0x0000FF00) << 8) |
                   ((value & 0x00FF0000) >> 8) |
                   ((value & 0xFF000000) >> 24);
        }

        private static ulong SwapBytes(ulong value)
        {
            return  ((value & 0x00000000000000FF) << 56) |
                    ((value & 0x000000000000FF00) << 40) |
                    ((value & 0x0000000000FF0000) << 24) |
                    ((value & 0x00000000FF000000) << 8) |
                    ((value & 0x000000FF00000000) >> 8) |
                    ((value & 0x0000FF0000000000) >> 24) |
                    ((value & 0x00FF000000000000) >> 40) |
                    ((value & 0xFF00000000000000) >> 56);
        }
    }

    /// <summary>
    /// A BinaryWriter that handles endianness for different platforms
    /// </summary>
    public class EndianWriter : BinaryWriter
    {
        private readonly bool m_bigEndian;

        public EndianWriter(Stream stream, bool bigEndian) : base(stream)
        {
            m_bigEndian = bigEndian;
        }

        public EndianWriter(Stream stream, EndianType endianType) : base(stream)
        {
            m_bigEndian = endianType == EndianType.BigEndian;
        }

        public EndianWriter(Stream stream, Encoding encoding, bool bigEndian) : base(stream, encoding)
        {
            m_bigEndian = bigEndian;
        }

        public EndianWriter(Stream stream, Encoding encoding, bool leaveOpen, bool bigEndian) : base(stream, encoding, leaveOpen)
        {
            m_bigEndian = bigEndian;
        }

        public override void Write(short value)
        {
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            base.Write(value);
        }

        public override void Write(int value)
        {
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            base.Write(value);
        }

        public override void Write(long value)
        {
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            base.Write(value);
        }

        public override void Write(ushort value)
        {
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            base.Write(value);
        }

        public override void Write(uint value)
        {
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            base.Write(value);
        }

        public override void Write(ulong value)
        {
            if (m_bigEndian)
            {
                value = SwapBytes(value);
            }
            base.Write(value);
        }

        public override void Write(float value)
        {
            if (m_bigEndian)
            {
                byte[] bytes = BitConverter.GetBytes(value);
                Array.Reverse(bytes);
                base.Write(bytes);
            }
            else
            {
                base.Write(value);
            }
        }

        public override void Write(double value)
        {
            if (m_bigEndian)
            {
                byte[] bytes = BitConverter.GetBytes(value);
                Array.Reverse(bytes);
                base.Write(bytes);
            }
            else
            {
                base.Write(value);
            }
        }

        private static short SwapBytes(short value)
        {
            return (short)(((ushort)value >> 8) | ((ushort)value << 8));
        }

        private static int SwapBytes(int value)
        {
            return (int)(((value & 0x000000FF) << 24) |
                   ((value & 0x0000FF00) << 8) |
                   ((value & 0x00FF0000) >> 8) |
                   ((value & 0xFF000000) >> 24));
        }

        private static long SwapBytes(long value)
        {
            ulong uValue = (ulong)value;
            return (long)(
                    ((uValue & 0x00000000000000FF) << 56) |
                    ((uValue & 0x000000000000FF00) << 40) |
                    ((uValue & 0x0000000000FF0000) << 24) |
                    ((uValue & 0x00000000FF000000) << 8) |
                    ((uValue & 0x000000FF00000000) >> 8) |
                    ((uValue & 0x0000FF0000000000) >> 24) |
                    ((uValue & 0x00FF000000000000) >> 40) |
                    ((uValue & 0xFF00000000000000) >> 56)
                );
        }

        private static ushort SwapBytes(ushort value)
        {
            return (ushort)((value >> 8) | (value << 8));
        }

        private static uint SwapBytes(uint value)
        {
            return ((value & 0x000000FF) << 24) |
                   ((value & 0x0000FF00) << 8) |
                   ((value & 0x00FF0000) >> 8) |
                   ((value & 0xFF000000) >> 24);
        }

        private static ulong SwapBytes(ulong value)
        {
            return  ((value & 0x00000000000000FF) << 56) |
                    ((value & 0x000000000000FF00) << 40) |
                    ((value & 0x0000000000FF0000) << 24) |
                    ((value & 0x00000000FF000000) << 8) |
                    ((value & 0x000000FF00000000) >> 8) |
                    ((value & 0x0000FF0000000000) >> 24) |
                    ((value & 0x00FF000000000000) >> 40) |
                    ((value & 0xFF00000000000000) >> 56);
        }
    }
}