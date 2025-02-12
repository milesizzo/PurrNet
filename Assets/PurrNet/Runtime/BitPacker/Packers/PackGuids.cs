using System;
using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class PackGuids
    {
        [UsedByIL]
        public static void Write(this BitPacker packer, Guid data)
        {
            // TODO: figure out how to do this without allocating (i.e. Guid.TryWriteBytes with static byte[16])
            packer.WriteBytes(data.ToByteArray());
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Guid data)
        {
            var upper64 = packer.ReadBits(64);
            var lower64 = packer.ReadBits(64);
            data = new Guid(
                (uint)(upper64 & 0xffffffff),
                (ushort)((upper64 >> 32) & 0xffff),
                (ushort)((upper64 >> 48) & 0xffff),
                (byte)(lower64 & 0xff),
                (byte)((lower64 >> 8) & 0xff),
                (byte)((lower64 >> 16) & 0xff),
                (byte)((lower64 >> 24) & 0xff),
                (byte)((lower64 >> 32) & 0xff),
                (byte)((lower64 >> 40) & 0xff),
                (byte)((lower64 >> 48) & 0xff),
                (byte)((lower64 >> 56) & 0xff)
            );
        }
    }
}
