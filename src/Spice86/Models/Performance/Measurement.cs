namespace Spice86.Models.Performance;

public class Measurement
    {
        public double Time { get; set; }
        public double Value { get; set; }

        public override string ToString()
        {
            return $"{Time:#0.0} {Value:##0.0}";
        }
    }