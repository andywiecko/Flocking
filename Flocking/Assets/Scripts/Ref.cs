using System;

namespace andywiecko.Flocking
{
    public class Ref<T> : IDisposable where T : IDisposable
    {
        public T Value;
        public Ref(T t) => Value = t;
        public void Dispose() => Value.Dispose();
        public static implicit operator T(Ref<T> @ref) => @ref.Value;
        public static implicit operator Ref<T>(T value) => new(value);
    }
}