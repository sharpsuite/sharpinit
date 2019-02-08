namespace SharpInit.Units
{
    public class RequirementDependency : Dependency
    {
        public override DependencyType Type => DependencyType.Requirement;
        public RequirementDependencyType RequirementType { get; set; }

        public RequirementDependency(string left, string right, string from, RequirementDependencyType type)
        {
            LeftUnit = left;
            RightUnit = right;
            SourceUnit = from;

            RequirementType = type;
        }

        public override string ToString()
        {
            return $"[{LeftUnit} -- ({RequirementType.ToString().ToLower()}) --> {RightUnit}; loaded by {SourceUnit}]";
        }
    }

    public enum RequirementDependencyType
    {
        Requires, // Left requires Right
        Wants, // Left wants Right
        Requisite, // Right is a requisite of Left
        BindsTo, // Left binds to Right
        PartOf, // Left is a part of Right
        Conflicts // Left conflicts with Right

    }
}
