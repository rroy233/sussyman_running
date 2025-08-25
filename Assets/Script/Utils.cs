using Google.Protobuf.WellKnownTypes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class Utils : MonoBehaviour
{

    [DllImport("User32.dll", SetLastError = true, ThrowOnUnmappableChar = true, CharSet = CharSet.Auto)]
    public static extern int MessageBox(IntPtr handle, string message, string title, int type);


    // Start is called before the first frame update
    public static long GetUnixMicro()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()*1000;
    }

    public static long GetUnixMill()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

}
