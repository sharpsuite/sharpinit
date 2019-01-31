using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    public class ServiceUnit : Unit
    {
        public new ServiceUnitFile File { get; set; }

        public override UnitFile GetUnitFile() => (UnitFile)File;

        public ServiceUnit(string path) : base(path)
        {
        }

        public override void LoadUnitFile(string path)
        {
            File = UnitParser.Parse<ServiceUnitFile>(path);
        }

        public override void Activate()
        {
            if (CurrentState == UnitState.Active || CurrentState == UnitState.Activating || CurrentState == UnitState.Failed)
                return;

            SetState(UnitState.Activating);

            // TODO: actually start the service here

            SetState(UnitState.Active);
        }

        public override void Deactivate()
        {
            if (CurrentState == UnitState.Inactive || CurrentState == UnitState.Deactivating || CurrentState == UnitState.Failed)
                return;

            SetState(UnitState.Deactivating);

            // TODO: actually stop the service here

            SetState(UnitState.Inactive);
        }
    }
}
