namespace MedievalZen.Player
{
    /// <summary>Viewpoint of the <see cref="PlayerCameraRig"/>.</summary>
    public enum CameraMode
    {
        /// <summary>Camera at the eyes; the body only casts its shadow.</summary>
        FirstPerson = 0,

        /// <summary>Orbit camera behind the shoulder.</summary>
        ThirdPerson = 1,
    }
}
