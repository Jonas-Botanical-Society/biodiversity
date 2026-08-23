using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;

namespace biodiversity.src.BlockBehaviors
{
    internal class BlockBehaviorThornyBlock : BlockBehavior
    {
        public BlockBehaviorThornyBlock(Block block) : base(block)
        {
            this.block = block;
        }
    }
}
