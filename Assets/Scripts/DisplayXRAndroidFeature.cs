using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

// Android: makes Unity's OpenXR plug-in safe with DisplayXR's runtime (v2.51-2.52).
//
// Unity calls xrCreateVulkanDeviceKHR twice, and on the second call a VkDeviceQueueCreateInfo it passes has a
// garbage pNext (0x8000000000 on the Nubia Pad 3D II). The runtime walks every queue create info's pNext chain
// looking for a global-priority struct (#1905) and crashes on it (SIGSEGV in oxr_vk_create_vulkan_device).
// This feature intercepts xrCreateVulkanDeviceKHR and hands the runtime a copy of the create info whose queue
// infos have no pNext. Unity chains nothing there that the device needs. It also answers
// xrSetAndroidApplicationThreadKHR itself (see GetProcAddr).
//
// And it declares the app's display rig (XR_DXR_view_rig) on every xrLocateViews: the virtual display at the
// XR space's origin, Height app units tall, eye spread IpdFactor. The runtime then returns render-ready eye
// poses and off-axis projections in scene units, so zoom and 3D strength work as on Windows (FourDVOrbit sets
// both every frame).
#if UNITY_EDITOR
[OpenXRFeature(UiName = "DisplayXR Android compatibility",
    BuildTargetGroups = new[] { BuildTargetGroup.Android },
    Company = "DisplayXR Demos",
    Desc = "Sanitizes Unity's Vulkan device create info for DisplayXR's Android runtime.",
    Version = "1.0.0",
    OpenxrExtensionStrings = ViewRigExtension,
    FeatureId = FeatureId)]
#endif
public class DisplayXRAndroidFeature : OpenXRFeature
{
    public const string FeatureId = "com.displayxrdemos.fourdv.displayxr-android";
    public const string ViewRigExtension = "XR_DXR_view_rig";
    const int XR_TYPE_DISPLAY_RIG_DXR = 1004999140;

    // Display rig, set by FourDVOrbit each frame (read on whichever thread Unity locates views).
    public static volatile float Height = 1f;
    public static volatile float IpdFactor = 1f;
    public static bool ViewRigActive { get; private set; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int LocateViewsFn(ulong session, IntPtr viewLocateInfo, IntPtr viewState, uint capacity, IntPtr countOutput, IntPtr views);
    static LocateViewsFn s_NextLocateViews;
    static readonly LocateViewsFn s_LocateViews = LocateViews;

    protected override bool OnInstanceCreate(ulong xrInstance)
    {
        ViewRigActive = UnityEngine.XR.OpenXR.OpenXRRuntime.IsExtensionEnabled(ViewRigExtension);
        Debug.Log($"[4DV] DisplayXR view rig {(ViewRigActive ? "on" : "not offered by the runtime")}");
        return true;
    }

    // XrViewLocateInfo (40 bytes, next at 8) gets a copy whose next is an XrDisplayRigDXR (64 bytes: type,
    // next, XrPosef pose at 16 (orientation xyzw, position xyz), then virtualDisplayHeight, ipdFactor,
    // parallaxFactor, perspectiveFactor at 44..56).
    [MonoPInvokeCallback(typeof(LocateViewsFn))]
    static int LocateViews(ulong session, IntPtr viewLocateInfo, IntPtr viewState, uint capacity, IntPtr countOutput, IntPtr views)
    {
        if (!ViewRigActive || viewLocateInfo == IntPtr.Zero)
            return s_NextLocateViews(session, viewLocateInfo, viewState, capacity, countOutput, views);
        IntPtr info = Copy(viewLocateInfo, 40);
        IntPtr rig = Marshal.AllocHGlobal(64);
        try
        {
            var zero = new byte[64];
            Marshal.Copy(zero, 0, rig, 64);
            Marshal.WriteInt32(rig, 0, XR_TYPE_DISPLAY_RIG_DXR);
            Marshal.WriteIntPtr(rig, 8, Marshal.ReadIntPtr(viewLocateInfo, 8));
            WriteFloat(rig, 28, 1f);                                   // orientation w (identity)
            WriteFloat(rig, 44, Mathf.Max(Height, 1e-3f));
            WriteFloat(rig, 48, Mathf.Clamp01(IpdFactor));
            WriteFloat(rig, 52, 1f);                                   // parallaxFactor
            WriteFloat(rig, 56, 1f);                                   // perspectiveFactor
            Marshal.WriteIntPtr(info, 8, rig);
            return s_NextLocateViews(session, info, viewState, capacity, countOutput, views);
        }
        finally
        {
            Marshal.FreeHGlobal(info);
            Marshal.FreeHGlobal(rig);
        }
    }

    static void WriteFloat(IntPtr p, int offset, float v) =>
        Marshal.WriteInt32(p, offset, BitConverter.ToInt32(BitConverter.GetBytes(v), 0));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int GetInstanceProcAddrFn(ulong instance, IntPtr name, IntPtr function);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int CreateVulkanDeviceFn(ulong instance, IntPtr createInfo, IntPtr vulkanDevice, IntPtr vulkanResult);

    static GetInstanceProcAddrFn s_NextGetProcAddr;
    static CreateVulkanDeviceFn s_NextCreateDevice;
    // Kept alive for as long as native code holds their function pointers.
    static readonly GetInstanceProcAddrFn s_GetProcAddr = GetProcAddr;
    static readonly CreateVulkanDeviceFn s_CreateDevice = CreateDevice;
    static readonly SetAndroidApplicationThreadFn s_SetThread = SetAndroidApplicationThread;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int SetAndroidApplicationThreadFn(ulong session, int threadType, uint threadId);

    [MonoPInvokeCallback(typeof(SetAndroidApplicationThreadFn))]
    static int SetAndroidApplicationThread(ulong session, int threadType, uint threadId) => 0;   // XR_SUCCESS

    protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
    {
        s_NextGetProcAddr = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddrFn>(func);
        return Marshal.GetFunctionPointerForDelegate(s_GetProcAddr);
    }

    [MonoPInvokeCallback(typeof(GetInstanceProcAddrFn))]
    static int GetProcAddr(ulong instance, IntPtr name, IntPtr function)
    {
        int result = s_NextGetProcAddr(instance, name, function);
        if (result != 0 || function == IntPtr.Zero) return result;
        string fn = Marshal.PtrToStringAnsi(name);
        if (fn == "xrSetAndroidApplicationThreadKHR")
        {
            // The runtime's handler calls a null compositor hook (SIGSEGV in
            // oxr_session_android_thread_settings). It's a scheduling hint only: accept it and do nothing.
            Marshal.WriteIntPtr(function, Marshal.GetFunctionPointerForDelegate(s_SetThread));
            return result;
        }
        if (fn == "xrLocateViews")
        {
            s_NextLocateViews = Marshal.GetDelegateForFunctionPointer<LocateViewsFn>(Marshal.ReadIntPtr(function));
            Marshal.WriteIntPtr(function, Marshal.GetFunctionPointerForDelegate(s_LocateViews));
            return result;
        }
        if (fn != "xrCreateVulkanDeviceKHR") return result;
        var next = Marshal.ReadIntPtr(function);
        if (next == IntPtr.Zero) return result;
        s_NextCreateDevice = Marshal.GetDelegateForFunctionPointer<CreateVulkanDeviceFn>(next);
        Marshal.WriteIntPtr(function, Marshal.GetFunctionPointerForDelegate(s_CreateDevice));
        return result;
    }

    // 64-bit layouts: XrVulkanDeviceCreateInfoKHR (64 bytes, vulkanCreateInfo at 48), VkDeviceCreateInfo
    // (72 bytes, queueCreateInfoCount at 20, pQueueCreateInfos at 24), VkDeviceQueueCreateInfo (40 bytes,
    // pNext at 8).
    [MonoPInvokeCallback(typeof(CreateVulkanDeviceFn))]
    static int CreateDevice(ulong instance, IntPtr createInfo, IntPtr vulkanDevice, IntPtr vulkanResult)
    {
        if (createInfo == IntPtr.Zero) return s_NextCreateDevice(instance, createInfo, vulkanDevice, vulkanResult);
        IntPtr vkInfo = Marshal.ReadIntPtr(createInfo, 48);
        if (vkInfo == IntPtr.Zero) return s_NextCreateDevice(instance, createInfo, vulkanDevice, vulkanResult);
        int queueCount = Marshal.ReadInt32(vkInfo, 20);
        IntPtr queueInfos = Marshal.ReadIntPtr(vkInfo, 24);

        IntPtr xrCopy = Copy(createInfo, 64), vkCopy = Copy(vkInfo, 72), queues = IntPtr.Zero;
        try
        {
            if (queueCount > 0 && queueInfos != IntPtr.Zero)
            {
                queues = Copy(queueInfos, 40 * queueCount);
                for (int i = 0; i < queueCount; i++)
                {
                    var pNext = Marshal.ReadIntPtr(queues, 40 * i + 8);
                    if (pNext != IntPtr.Zero)
                        Debug.Log($"[4DV] xrCreateVulkanDeviceKHR: dropping queue {i} pNext 0x{pNext.ToInt64():x}");
                    Marshal.WriteIntPtr(queues, 40 * i + 8, IntPtr.Zero);
                }
                Marshal.WriteIntPtr(vkCopy, 24, queues);
            }
            Marshal.WriteIntPtr(xrCopy, 48, vkCopy);
            return s_NextCreateDevice(instance, xrCopy, vulkanDevice, vulkanResult);
        }
        finally
        {
            Marshal.FreeHGlobal(xrCopy);
            Marshal.FreeHGlobal(vkCopy);
            if (queues != IntPtr.Zero) Marshal.FreeHGlobal(queues);
        }
    }

    static IntPtr Copy(IntPtr source, int size)
    {
        var bytes = new byte[size];
        Marshal.Copy(source, bytes, 0, size);
        var copy = Marshal.AllocHGlobal(size);
        Marshal.Copy(bytes, 0, copy, size);
        return copy;
    }
}
