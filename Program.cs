using System.Text;
using System.Runtime.InteropServices;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var str = "HID キーボード デバイス";
var bytesUTF16 = Encoding.Unicode.GetBytes(str);
var bytesUTF8 = new UTF8Encoding(false).GetBytes(str);
var bytesSJIS = Encoding.GetEncoding("Shift_JIS").GetBytes(str);

unsafe
{
    fixed (byte* pUTF16 = &bytesUTF16[0], pUTF8 = &bytesUTF8[0], pSJIS = &bytesSJIS[0])
    {
        Console.WriteLine($"Marshal.PtrToStringUni(UTF-16):{Marshal.PtrToStringUni((IntPtr)pUTF16)}");
        Console.WriteLine($"Marshal.PtrToStringUni(UTF-8):{Marshal.PtrToStringUni((IntPtr)pUTF8)}");
        Console.WriteLine($"Marshal.PtrToStringUni(Shift-JIS):{Marshal.PtrToStringUni((IntPtr)pSJIS)}");
        Console.WriteLine($"Marshal.PtrToStringAnsi(UTF-16):{Marshal.PtrToStringAnsi((IntPtr)pUTF16)}");
        Console.WriteLine($"Marshal.PtrToStringAnsi(UTF-8):{Marshal.PtrToStringAnsi((IntPtr)pUTF8)}");
        Console.WriteLine($"Marshal.PtrToStringAnsi(Shift-JIS):{Marshal.PtrToStringAnsi((IntPtr)pSJIS)}");
        Console.WriteLine($"EncodingDetector.DecodeAuto(UTF-16):{EncodingDetector.DecodeAuto(bytesUTF16)}");
        Console.WriteLine($"EncodingDetector.DecodeAuto(UTF-8):{EncodingDetector.DecodeAuto(bytesUTF8)}");
        Console.WriteLine($"EncodingDetector.DecodeAuto(Shift-JIS):{EncodingDetector.DecodeAuto(bytesSJIS)}");
    }
}
