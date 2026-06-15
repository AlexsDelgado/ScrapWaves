/// <summary>
/// Contrato para cualquier item del mundo que pueda recogerse al acercarse el jugador.
/// <see cref="WorldPickup"/> gestiona el movimiento; el implementador solo define qué pasa al recoger.
/// </summary>
public interface IPickable
{
    void OnPickedUp();
}
