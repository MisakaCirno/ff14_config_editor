using FF14ConfigEditor.UISave;

namespace FF14ConfigEditor.Tests;

public class UtilsTests
{
    [Fact]
    public void EncryptData_XorsEachByteWithDefaultKey()
    {
        byte[] plainData = [0x00, 0x31, 0xFF, 0x42];

        byte[] encryptedData = Utils.EncryptData(plainData);

        Assert.Equal([0x31, 0x00, 0xCE, 0x73], encryptedData);
        Assert.NotSame(plainData, encryptedData);
    }

    [Fact]
    public void EncryptAndDecryptData_AreSymmetric()
    {
        byte[] plainData = [0x00, 0x01, 0x31, 0x7F, 0x80, 0xFF];

        byte[] encryptedData = Utils.EncryptData(plainData);
        byte[] decryptedData = Utils.DecryptData(encryptedData);

        Assert.Equal(plainData, decryptedData);
    }

    [Fact]
    public void EncryptAndDecryptData_AreSymmetricWithCustomKey()
    {
        byte xorKey = 0x5A;
        byte[] plainData = [0x12, 0x34, 0x56, 0x78];

        byte[] encryptedData = Utils.EncryptData(plainData, xorKey);
        byte[] decryptedData = Utils.DecryptData(encryptedData, xorKey);

        Assert.Equal(plainData, decryptedData);
    }
}
