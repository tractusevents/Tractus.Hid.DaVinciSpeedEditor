using HidSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tractus.Hid.DaVinciSpeedEditor;

/// <summary>
/// A very early C# implementation of a wrapper class
/// for working with the BlackMagic DaVinci Speed Editor.
/// 
/// The Speed Editor shows up as a USB HID device, but
/// it requires some authentication before you can read
/// from it, or control the LED status.
/// </summary>
/// <remarks>
/// The following Github repos laid the foundation for this - 
/// I only took what they released and translated it into
/// C#. @smunaut, @davidgiven, and @haavard15 did all 
/// the hard work, and to them, we should all give
/// our gratitude.
/// 
/// https://github.com/Haavard15/SpeedEditorHID/tree/test
/// https://github.com/davidgiven/bmdkey?tab=readme-ov-file
/// https://github.com/smunaut/blackmagic-misc
/// 
/// https://www.youtube.com/watch?v=UoIlwze5xp4
/// </remarks>
public class DaVinciSpeedEditor
{
    private static readonly int VendorId = 0x1edb; // DaVinci vendor ID
    private static readonly int ProductId = 0xda0e; // Speed Editor product ID
    private readonly HidDevice device;
    private readonly HidStream stream;

    public DaVinciSpeedEditor()
    {
        var deviceList = DeviceList.Local;
        var device = deviceList?.GetHidDevices(VendorId, ProductId)?.FirstOrDefault();
        if (device == null)
        {
            throw new Exception("Device not found.");
        }

        this.device = device;
        this.stream = this.device.Open();
        this.stream.ReadTimeout = Timeout.Infinite;
    }

    // I had ChatGPT translate this Python code...
    // https://github.com/smunaut/blackmagic-misc
    // ...into C#.
    //
    // I have no clue why it works, but it works.
    //
    // This was also a HUGE resource to make this work at all.
    // https://github.com/davidgiven/bmdkey?tab=readme-ov-file
    // and this.
    // https://github.com/Haavard15/SpeedEditorHID/blob/test/speedEditor.js
    public void Authenticate()
    {
        this.SendFeatureReport(new byte[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        var challengeFromKeyboard = this.ReceiveFeatureReport(6, 10);

        this.SendFeatureReport(new byte[] { 6, 1, 0, 0, 0, 0, 0, 0, 0, 0 });
        this.ReceiveFeatureReport(6, 10);

        var responseToKeyboard = new byte[10];
        responseToKeyboard[0] = 6;
        responseToKeyboard[1] = 3;
        PutInt64(responseToKeyboard, 2, CalculateKeyboardResponse(GetInt64(challengeFromKeyboard, 2)));

        this.SendFeatureReport(responseToKeyboard);

        var result = this.ReceiveFeatureReport(6, 10);
        if (result[0] != 6 || result[1] != 4)
        {
            throw new Exception("Unable to authenticate keyboard.");
        }
        Console.WriteLine("Authenticated");
    }

    private static ulong CalculateKeyboardResponse(ulong challenge)
    {
        ulong[] authEvenTable = { 0x3ae1206f97c10bc8, 0x2a9ab32bebf244c6, 0x20a6f8b8df9adf0a, 0xaf80ece52cfc1719, 0xec2ee2f7414fd151, 0xb055adfd73344a15, 0xa63d2e3059001187, 0x751bf623f42e0dde };
        ulong[] authOddTable = { 0x3e22b34f502e7fde, 0x24656b981875ab1c, 0xa17f3456df7bf8c3, 0x6df72e1941aef698, 0x72226f011e66ab94, 0x3831a3c606296b42, 0xfd7ff81881332c89, 0x61a3f6474ff236c6 };
        ulong mask = 0xa79a63f585d37bf0;

        ulong n = challenge & 7;
        ulong v = Rol8n(challenge, (int)n);

        ulong k;
        if ((v & 1) == ((0x78UL >> (int)n) & 1))
        {
            k = authEvenTable[n];
        }
        else
        {
            v ^= Rol8(v);
            k = authOddTable[n];
        }

        return v ^ (Rol8(v) & mask) ^ k;
    }

    private static ulong Rol8(ulong value)
    {
        return ((value << 56) | (value >> 8)) & 0xffffffffffffffff;
    }

    private static ulong Rol8n(ulong value, int n)
    {
        for (int i = 0; i < n; i++)
        {
            value = Rol8(value);
        }
        return value;
    }


    private byte[] ReceiveFeatureReport(int reportId, int length)
    {
        byte[] buffer = new byte[length];
        buffer[0] = (byte)reportId;
        this.stream.GetFeature(buffer); // Use GetFeature to receive feature reports
        return buffer;
    }

    private void SendFeatureReport(byte[] report)
    {
        this.stream.SetFeature(report); // Use SetFeature to send feature reports
    }

    private static ulong GetInt64(byte[] data, int startIndex)
    {
        return BitConverter.ToUInt64(data, startIndex);
    }

    private static void PutInt64(byte[] data, int startIndex, ulong value)
    {
        Array.Copy(BitConverter.GetBytes(value), 0, data, startIndex, 8);
    }

    public void Run()
    {
        try
        {
            this.Authenticate();

            // Example of sending commands to the device after authentication
            this.stream.Write(new byte[] { 3, 0x0, 0, 0, 0, 0, 0 });
            this.stream.Write(new byte[] { 4, 0xFF });


            // Command 2 seems to control the LED status
            // There are 21 LEDs on board.
            // 18 of them are controlled with this array.
            this.stream.Write(new byte[] { 2, 0xFF, 0xFF, 0xFF, 0xFF });

            // This code is me trying to figure out what LEDs do what.
            // 
            // 0b11111111 = Each bit controls an LED on or off.
            var testBuffer = new byte[] { 2, 0xFF, 0xFF, 0xFF, 0xFF };

            do
            {
                Console.WriteLine($"Current buffer[1] {testBuffer[1]:B}");
                this.stream.Write(testBuffer);
                testBuffer[1] = (byte)(testBuffer[1] >> 1);
                Thread.Sleep(100);
            } while (testBuffer[1] != 0x0);
            this.stream.Write(testBuffer);

            do
            {
                Console.WriteLine($"Current buffer[2] {testBuffer[2]:B}");
                this.stream.Write(testBuffer);
                testBuffer[2] = (byte)(testBuffer[2] >> 1);
                Thread.Sleep(100);
            } while (testBuffer[2] != 0x0);
            this.stream.Write(testBuffer);

            // Index [3] seems to control the Cam3 and Audio Only LEDs
            do
            {
                Console.WriteLine($"Current buffer[3] {testBuffer[3]:B}");
                this.stream.Write(testBuffer);
                testBuffer[3] = (byte)(testBuffer[3] >> 1);
                Thread.Sleep(500);
            } while (testBuffer[3] != 0x0);
            this.stream.Write(testBuffer);

            // Start reading and processing input from the device
            this.ReadDeviceData();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }

    private void ReadDeviceData()
    {
        byte[] buffer = new byte[64];
        while (true)
        {
            int bytesRead = this.stream.Read(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                this.ProcessData(buffer.Take(bytesRead).ToArray());
            }
        }
    }

    private void ProcessData(byte[] data)
    {
        // Interpret data packets based on the device’s response structure
        if (data[0] == 3)
        {
            // Handle wheel data, such as updating UI or sending control signals
            var delta = BitConverter.ToInt32(data, 2);
            Console.WriteLine("Wheel movement: " + delta);
        }
        else if (data[0] == 4)
        {
            // Handle button data
            Console.WriteLine("Button packet received");
            for (int i = 0; i < 6; i++)
            {
                var keycode = BitConverter.ToUInt16(data, 1 + i * 2);
                if (keycode != 0)
                {
                    Console.WriteLine("Button pressed: " + keycode);
                }
            }
        }
        else
        {
            Console.WriteLine("Unknown packet type: " + data[0]);
        }
    }
}
