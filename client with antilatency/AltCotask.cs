using Antilatency.Alt.Tracking;
//using UnityEngine;

/// <summary>
/// AltCotask is used as a wrapper for the safe handling of a ITrackingCotask instance.
/// </summary>
public class AltCotask : IAltCotask
{
    private ITrackingCotask internalCotask;
    private float timeSinceLastStableReading;

    /// <summary>
    /// Constructs the AltCotask instance and assigns the ITrackingCotask instance.
    /// </summary>
    /// <param name="cotask">ITrackingCotask instance to assign.</param>
    public AltCotask(ITrackingCotask cotask)
    {
        internalCotask = cotask;
        timeSinceLastStableReading = 0f;
    }

    /// <summary>
    /// Checks the disposed state of the ITrackingCotask and disposes it if it hasn't been disposed already.
    /// Sets the internalCotask to null once it has been disposed to indicate that is has been disposed.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed()) return;
        Antilatency.Utils.SafeDispose(ref internalCotask);
        internalCotask = null;
    }

    /// <summary>
    /// Checks if the internalCotask has been disposed.
    /// </summary>
    /// <returns>Returns a Boolean indicating whether or not the internalCotask has been disposed.</returns>
    public bool IsDisposed()
    {
        return internalCotask == null;
    }

    /// <summary>
    /// Checks the current state of the internalCotask's task.
    /// </summary>
    /// <returns>Returns a Boolean indicating whether or not the internalCotask's task has finished.</returns>
    public bool IsFinished()
    {
        if (IsDisposed()) return true;
        else return internalCotask.isTaskFinished();
    }

    /// <summary>
    /// Gets the real time since the last stable reading was taken from the cotask.
    /// </summary>
    /// <returns>Returns a float time stamp of the last time a stable reading was taken from the cotask.</returns>
    public float GetTimeSinceLastStableReading() { return timeSinceLastStableReading; }

    /*
    /// <summary>
    /// Gets the extrapolated state using the given Pose placement and Float extrapolationTime.
    /// </summary>
    /// <param name="state">Stores the state collected from the internalCotask.</param>
    /// <param name="placement">Determines the distance between the tracker and the centre eye device.</param>
    /// <param name="extrapolationTime">Determines the time between two tracking systems.</param>
    /// <returns>Returns a Boolean indicating if the state has been successfully collected.</returns>
    public bool GetExtrapolatedState(out State state, Pose placement, float extrapolationTime)
    {
        state = new State();
        if (IsDisposed()) return false;

        state = internalCotask.getExtrapolatedState(placement, extrapolationTime);

        //if (state.stability.stage == Stage.Tracking6Dof) timeSinceLastStableReading = Time.realtimeSinceStartup;

        return true;
    }
    */

    /// <summary>
    /// Gets the state using the given Float angularVelocityAvgTimeInSeconds.
    /// </summary>
    /// <param name="state">Stores the state collected from the internalCotask.</param>
    /// <param name="angularVelocityAvgTimeInSeconds">Determines the average velocity of the tracker.</param>
    /// <returns>Returns a Boolean indicating if the state has been successfully collected.</returns>
    public bool GetState(out State state, float angularVelocityAvgTimeInSeconds)
    {
        state = new State();
        if (IsDisposed()) return false;

        state = internalCotask.getState(angularVelocityAvgTimeInSeconds);

        //if (state.stability.stage == Stage.Tracking6Dof) timeSinceLastStableReading = Time.realtimeSinceStartup;

        return true;
    }


}