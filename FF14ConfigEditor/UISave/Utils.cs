using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF14ConfigEditor.UISave
{
    public class Utils
    {
        public static byte[] DecryptData(byte[] encryptedData, byte xor_key = 0x31)
        {
            ArgumentNullException.ThrowIfNull(encryptedData);

            byte[] decryptedData = new byte[encryptedData.Length];
            for (int i = 0; i < encryptedData.Length; i++)
            {
                decryptedData[i] = (byte)(encryptedData[i] ^ xor_key);
            }
            return decryptedData;
        }

        public static byte[] EncryptData(byte[] plainData, byte xor_key = 0x31)
        {
            ArgumentNullException.ThrowIfNull(plainData);

            byte[] encryptedData = new byte[plainData.Length];
            for (int i = 0; i < plainData.Length; i++)
            {
                encryptedData[i] = (byte)(plainData[i] ^ xor_key);
            }
            return encryptedData;
        }
    }
}
