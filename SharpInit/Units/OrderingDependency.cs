namespace SharpInit.Units
{
    /// <summary>
    /// An ordering dependency.
    /// </summary>
    public class OrderingDependency : Dependency
    {
        public override DependencyType Type => DependencyType.Ordering;
        public OrderingDependencyType OrderingType { get; set; }

        public OrderingDependency(string left, string right, string from, OrderingDependencyType type = OrderingDependencyType.After)
        {
            LeftUnit = left;
            RightUnit = right;
            SourceUnit = from;

            OrderingType = type;
        }

        public override string ToString()
        {
            return $"[{LeftUnit} -- ({OrderingType.ToString().ToLower()}) --> {RightUnit}; loaded by {SourceUnit}]";
        }
    }
    
    public enum OrderingDependencyType
    {
        After // Left after Right
    }
}
