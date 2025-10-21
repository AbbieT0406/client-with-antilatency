using Antilatency.Alt.Tracking;

public interface IAltCotask
{
    bool GetState(out State state, float angularVelocityAvgTimeInSeconds);
}