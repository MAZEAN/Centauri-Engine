namespace Centauri.Simulation.Physics;

using System.Numerics;
using System.Runtime.CompilerServices;

using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

// Narrow-phase callbacks decide which collidable pairs generate contacts and with what material.
// This is the minimal general-purpose implementation from the BEPU demos: every pair involving a
// dynamic body collides, with a single shared friction/bounce material. Tuning per-material
// behaviour would happen here later, keyed off CollidableReference.
internal struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public SpringSettings ContactSpringiness;
    public float MaximumRecoveryVelocity;
    public float FrictionCoefficient;

    public NarrowPhaseCallbacks(SpringSettings contactSpringiness, float maximumRecoveryVelocity = 2f, float frictionCoefficient = 1f)
    {
        ContactSpringiness      = contactSpringiness;
        MaximumRecoveryVelocity = maximumRecoveryVelocity;
        FrictionCoefficient     = frictionCoefficient;
    }

    public void Initialize(Simulation simulation)
    {
        // A default-constructed struct (BEPU may create one before we set ours) gets sane values.
        if (ContactSpringiness.AngularFrequency == 0 && ContactSpringiness.TwiceDampingRatio == 0)
        {
            ContactSpringiness      = new SpringSettings(30, 1);
            MaximumRecoveryVelocity = 2f;
            FrictionCoefficient     = 1f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        => a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
        => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial.FrictionCoefficient     = FrictionCoefficient;
        pairMaterial.MaximumRecoveryVelocity = MaximumRecoveryVelocity;
        pairMaterial.SpringSettings          = ContactSpringiness;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
        => true;

    public readonly void Dispose() { }
}

// Pose-integrator callbacks apply per-body velocity integration each (sub)step — here, uniform
// gravity plus a little linear/angular damping so bodies eventually settle. SIMD-wide: the engine
// hands us up to Vector<float>.Count bodies at once, so gravity/damping are precomputed as wide
// vectors in PrepareForIntegration and applied without per-body scalar work.
internal struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public Vector3 Gravity;
    public float   LinearDamping;
    public float   AngularDamping;

    private Vector3Wide   _gravityWideDt;
    private Vector<float> _linearDampingDt;
    private Vector<float> _angularDampingDt;

    public PoseIntegratorCallbacks(Vector3 gravity, float linearDamping = 0.03f, float angularDamping = 0.03f) : this()
    {
        Gravity        = gravity;
        LinearDamping  = linearDamping;
        AngularDamping = angularDamping;
    }

    // Nonconserving is the cheap, stable default; velocity integration is per-substep so gravity is
    // scaled by the substep dt handed to PrepareForIntegration, not the whole frame's.
    public readonly AngularIntegrationMode AngularIntegrationMode      => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies           => false;
    public readonly bool IntegrateVelocityForKinematics               => false;

    public readonly void Initialize(Simulation simulation) { }

    public void PrepareForIntegration(float dt)
    {
        // Damping is a per-second fraction; raise to the dt power so the settle rate is
        // step-rate-independent (halving the timestep doesn't double the damping).
        _linearDampingDt  = new Vector<float>(MathF.Pow(Math.Clamp(1f - LinearDamping,  0f, 1f), dt));
        _angularDampingDt = new Vector<float>(MathF.Pow(Math.Clamp(1f - AngularDamping, 0f, 1f), dt));
        _gravityWideDt    = Vector3Wide.Broadcast(Gravity * dt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
        BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
    {
        velocity.Linear  = (velocity.Linear + _gravityWideDt) * _linearDampingDt;
        velocity.Angular = velocity.Angular * _angularDampingDt;
    }
}
