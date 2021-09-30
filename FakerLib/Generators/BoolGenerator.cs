using System;
using FakerLib.PluginSupport;

namespace FakerLib.Generators
{
    public class BoolGenerator : Generator<bool>
    {
        public override bool Generate()
        {
            return Convert.ToBoolean(new Random().Next() & 0b1);
        }
    }
}
