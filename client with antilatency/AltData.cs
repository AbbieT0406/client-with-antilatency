using Antilatency.Alt.Tracking;
using Antilatency.DeviceNetwork;
using HIVE.Commons.Flatbuffers.Generated;

/// <summary>
/// AltData stores information about an Antilatency Alt collected in the DataCollector.
/// </summary>
public class AltData
{
    /// <summary>
    /// Constructs an instance of AltData using the given arguments.
    /// </summary>
    /// <param name="id">Unique identifier for the alt's parent device</param>
    /// <param name="subscriptionType">What subscription type the alt has.</param>
    /// <param name="cotask">The ITrackingCotask associated with the alt.</param>
    /// <param name="node">The NodeHandle associated with the alt.</param>
    /// <param name="blueEnvironment">A Boolean indicating if the ITrackingCotask is initialized in the blue environment.</param>
    public AltData(ulong id, SubscriptionType subscriptionType, ITrackingCotask cotask, NodeHandle node, bool blueEnvironment)
    {
        Id = id;
        SubscriptionType = subscriptionType;
        Cotask = new AltCotask(cotask);
        Node = node;

        // init to null as needs to have "value" before exiting constructor
        //SmoothTrackingData = null;
        IsTrackingInitialized = false;
        IsBlueEnvironment = blueEnvironment;
    }

    /*
    /// <summary>
    /// Initializes smooth tracking on the given AltData instance.
    /// </summary>
    /// <param name="alt">The AltData instance to initialize smooth tracking on.</param>
    /// <param name="smoothTrackingData">The SmoothTrackingData instance to assign the AltData instance to.</param>
    public void InitializeSmoothTracking(ref AltData alt, SmoothTrackingData smoothTrackingData)
    {
        if (alt.IsTrackingInitialized) return;
        alt.SmoothTrackingData = smoothTrackingData;
        alt.IsTrackingInitialized = true;
    }
    */

    /// <summary>
    /// Gets the real time since the last stable reading was taken from the cotask.
    /// </summary>
    /// <returns>Returns a float time stamp of the last time a stable reading was taken from the cotask.</returns>
    public float GetTimeSinceLastStableReading() { return Cotask.GetTimeSinceLastStableReading(); }

    /// <summary>
    /// Property to store the alts unique identifier.
    /// </summary>
    public ulong Id { get; private set; }
    /// <summary>
    /// Property to store the alts subscription type.
    /// </summary>
    public SubscriptionType SubscriptionType { get; private set; }
    /// <summary>
    /// Property to store the AltCotask instance.
    /// </summary>
    public AltCotask Cotask { get; private set; }
    /// <summary>
    /// Property to store the alts unique NodeHandle.
    /// </summary>
    public NodeHandle Node { get; private set; }
    /// <summary>
    /// Property to store the alts SmoothTrackingData instance.
    /// </summary>
    //public SmoothTrackingData SmoothTrackingData { get; private set; }
    /// <summary>
    /// Property to determine the tracking state of the alt.
    /// </summary>
    public bool IsTrackingInitialized { get; private set; }
    /// <summary>
    /// Property to determine in what environment the alt was initialized.
    /// </summary>
    public bool IsBlueEnvironment { get; private set; }
}