
using Verse;

namespace Fortified;

public class PawnRenderNode_VehicleTurret : PawnRenderNode
{
    private readonly Pawn _pawn;
    private Thing _equipment;
    private CompVehicleWeapon _compVehicleWeapon;

    public PawnRenderNode_VehicleTurret(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree) : base(pawn, props, tree)
    {
        _pawn = pawn;
    }

    public bool TryGetCompVehicleWeapon(out CompVehicleWeapon compVehicleWeapon)
    {
        //cached
        if (_equipment == _pawn.equipment.Primary)
        {
            compVehicleWeapon = _compVehicleWeapon;
            return _compVehicleWeapon != null;
        }

        //try set cache
        _equipment = _pawn.equipment.Primary;
        if (_equipment == null)
        {
            compVehicleWeapon = null;
            return false;
        }
        _compVehicleWeapon = _equipment.TryGetComp<CompVehicleWeapon>();
        compVehicleWeapon = _compVehicleWeapon;
        return _compVehicleWeapon != null;
    }
}