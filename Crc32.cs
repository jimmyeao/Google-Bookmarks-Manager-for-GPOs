using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class Crc32 : HashAlgorithm
    {
        private static readonly uint[] Table;
        private uint _crc;

        static Crc32()
        {
            const uint polynomial = 0xedb88320;
            Table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 8; j > 0; j--)
                {
                    if ((crc & 1) == 1)
                        crc = (crc >> 1) ^ polynomial;
                    else
                        crc >>= 1;
                }
                Table[i] = crc;
            }
        }

        public Crc32()
        {
            HashSizeValue = 32;
        }

        public override void Initialize()
        {
            _crc = 0xffffffff;
        }

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            for (int i = ibStart; i < cbSize; i++)
            {
                byte index = (byte)((_crc & 0xff) ^ array[i]);
                _crc = (_crc >> 8) ^ Table[index];
            }
        }

        protected override byte[] HashFinal()
        {
            _crc = ~_crc;
            return BitConverter.GetBytes(_crc);
        }
    }
}
