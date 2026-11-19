using UnityEngine;
using VanguardZero.Core;

namespace VanguardZero.VFX
{
    /// <summary>
    /// Pooled ballistic brass shell casing ejected from weapon receiver.
    /// Lightweight kinematic/gravity simulation (zero Rigidbody overhead per shot).
    /// </summary>
    public class ShellCasingProjectile : MonoBehaviour
    {
        private Vector3 _velocity;
        private Vector3 _angularVel;
        private float _age;
        private float _lifetime = 2.6f;
        private bool _bounced;
        private float _floorY;

        public void Launch(Vector3 pos, Quaternion rot, Vector3 vel, float floorY)
        {
            transform.SetPositionAndRotation(pos, rot);
            _velocity = vel;
            _angularVel = new Vector3(
                UnityEngine.Random.Range(-720f, 720f),
                UnityEngine.Random.Range(-720f, 720f),
                UnityEngine.Random.Range(-720f, 720f));
            _age = 0f;
            _bounced = false;
            _floorY = floorY;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age >= _lifetime)
            {
                gameObject.SetActive(false);
                return;
            }

            _velocity.y -= 13.5f * dt;
            Vector3 next = transform.position + _velocity * dt;

            if (next.y <= _floorY)
            {
                next.y = _floorY;
                _velocity.y = Mathf.Abs(_velocity.y) * 0.38f;
                _velocity.x *= 0.62f;
                _velocity.z *= 0.62f;
                _angularVel *= 0.55f;

                if (!_bounced)
                {
                    _bounced = true;
                    VanguardAudioManager.Instance?.PlayRandom3D(
                        "shell_casing_concrete", 2, next, AudioBus.Foley, 0.28f, 0.12f, 16f);
                }
            }

            transform.position = next;
            transform.Rotate(_angularVel * dt, Space.Self);
        }
    }
}
