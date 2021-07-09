using System;
using System.Collections.Generic;

namespace SharpInit
{
    public class ActionThrottle
    {
        public TimeSpan Interval { get; set; }
        public int Burst { get; set; }

        public List<TimeSpan> RecordedActions = new List<TimeSpan>();

        private TimeSpan UnthrottledBy = default;

        public ActionThrottle(TimeSpan interval, int burst)
        {
            Interval = interval;
            Burst = burst;
        }

        public void Clear()
        {
            RecordedActions.Clear();
        }

        public bool IsThrottled()
        {
            if (Interval.TotalSeconds == 0)
                return false;

            if (Burst == 0)
                return false;

            if (RecordedActions.Count == 0)
                return false;
            
            // Walk back from the last recorded event.
            var current_time = Program.ElapsedSinceStartup();
            var furthest_back = current_time;

            for (int i = RecordedActions.Count - 1, actions = 0; i >= 0; i--, actions++)
            {
                var event_time = RecordedActions[i];

                if (event_time < furthest_back)
                    furthest_back = event_time;
                
                var time_delta = current_time - furthest_back;

                if (time_delta > Interval) 
                {
                    if (actions <= Burst + 1)
                    {
                        UnthrottledBy = current_time;

                        // do a little bit of cleanup
                        if (i > 1)
                            RecordedActions.RemoveRange(0, i - 1);
                        return false;
                    }
                    else
                    {
                        UnthrottledBy = furthest_back + Interval;
                        return true;
                    }
                }
                else
                {
                    if (actions >= Burst)
                    {
                        UnthrottledBy = furthest_back + Interval;
                        return true;
                    }
                }
            }

            if (RecordedActions.Count == 1)
            {
                if (Burst > 1)
                    return false;
                else
                    return (current_time - furthest_back) < Interval;
            }

            UnthrottledBy = TimeSpan.Zero;
            return true;
        }

        public void RecordAction(TimeSpan time = default)
        {
            if (time == default)
                time = Program.ElapsedSinceStartup();
            
            RecordedActions.Add(time);
        }
    }
}