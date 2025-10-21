using System;
using System.Threading;
using Antilatency.DeviceNetwork;
using Antilatency.Alt.Tracking;
using Antilatency.Alt.Environment.Selector;

namespace antilatency_getter
{
    public static class AntilatencyHandler
    {
        #region Initialization Methods
        public static INetwork CreateNetwork()
        {
            using var adnLibrary = Antilatency.DeviceNetwork.Library.load();
            if (adnLibrary == null)
            {
                throw new Exception("Failed to load AntilatencyDeviceNetwork library");
            }

            adnLibrary.setLogLevel(Antilatency.DeviceNetwork.LogLevel.Info);

            // create filter for all usb and ip devices
            using var filter = adnLibrary.createFilter();

            // Use fully qualified names to resolve ambiguity
            filter.addUsbDevice(Antilatency.DeviceNetwork.Constants.AllUsbDevices);
            filter.addIpDevice(
                Antilatency.DeviceNetwork.Constants.AllIpDevicesIp,
                Antilatency.DeviceNetwork.Constants.AllIpDevicesMask
            );

            var network = adnLibrary.createNetwork(filter);

            // Wait for devices to be detected
            Thread.Sleep(1000);

            return network;
        }

        public static Antilatency.Alt.Environment.IEnvironment CreateEnvironmentBlue(
            Antilatency.Alt.Environment.Selector.ILibrary environmentSelectorLibrary)
        {
            string environmentCode = "AntilatencyAltEnvironmentHorizontalGrid~AgAHB2q8dD8zM7M-abeyviGwsj4AAAAAAQAAQEAhsDI_AA0GAQMDAgEGAwMCAwIGBQMDBAEEBgIGBgIBBgIAAwMABQMEAwAAAQM";
            return environmentSelectorLibrary.createEnvironment(environmentCode);
        }

        public static Antilatency.Alt.Environment.IEnvironment CreateEnvironmentGreen(
            Antilatency.Alt.Environment.Selector.ILibrary environmentSelectorLibrary)
        {
            string environmentCode = "AntilatencyAltEnvironmentHorizontalGrid~AgAFBQgFXz_NzMw-Jo7OvZqZmT4AAAAAATMzM0CamRk_AAYABAMAAQEBAAIEAAIEAgMEBAI";
            return environmentSelectorLibrary.createEnvironment(environmentCode);
        }

        public static Antilatency.Math.floatP3Q CreatePlacement(
            Antilatency.Alt.Tracking.ILibrary trackingLibrary,
            string placementCode)
        {
            return trackingLibrary.createPlacement(placementCode);
        }
        #endregion
    }
}