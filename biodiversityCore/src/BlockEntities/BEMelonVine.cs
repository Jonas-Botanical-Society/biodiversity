using biodiversity.src.BlockBehaviors;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace biodiversity.src.BlockEntities
{
    internal class BEMelonVine : BlockEntity
    {
        public float melonHoursToGrow = 12f;

        public float vineHoursToGrow = 12f;

        public float vineHoursToGrowStage2 = 6f;

        public float bloomProbability = 0.5f;

        public float debloomProbability = 0.5f;

        public float vineSpawnProbability = 0.5f;

        public float preferredGrowthDirProbability = 0.75f;

        public int maxAllowedMelonGrowthTries = 3;

        public string melonBlockCode;
        public string domainCode;

        public long growListenerId;

        public Block stage1VineBlock;

        public Block melonBlock;

        public double totalHoursForNextStage;

        public bool canBloom;

        public int melonGrowthTries;

        public Dictionary<BlockFacing, double> melonTotalHoursForNextStage = new Dictionary<BlockFacing, double>();

        public BlockPos parentPlantPos;

        public BlockFacing preferredGrowthDir;

        public int internalStage;

        public BEMelonVine()
        {
            BlockFacing[] hORIZONTALS = BlockFacing.HORIZONTALS;
            foreach (BlockFacing key in hORIZONTALS)
            {
                melonTotalHoursForNextStage.Add(key, 0.0);

                
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);


            melonHoursToGrow = Block.Attributes["melonHoursToGrow"].AsFloat(12);
            vineHoursToGrow = Block.Attributes["vineHoursToGrowFirstStage"].AsFloat(12);
            vineHoursToGrowStage2 = Block.Attributes["vineHoursToGrowSecondStage"].AsFloat(6);
            bloomProbability = Block.Attributes["bloomProbability"].AsFloat(0.5f);
            debloomProbability = Block.Attributes["debloomProbability"].AsFloat(0.5f);
            vineSpawnProbability = Block.Attributes["vineSpawnProbability"].AsFloat(0.5f);
            preferredGrowthDirProbability = Block.Attributes["preferredGrowthDirProbability"].AsFloat(0.75f);
            maxAllowedMelonGrowthTries = Block.Attributes["maxAllowedMelonGrowthTries"].AsInt(3);
            melonBlockCode = Block.Attributes["melonBlockCode"].AsString();
            domainCode = Block.Code.ShortDomain();

            stage1VineBlock = Api.World.GetBlock(new AssetLocation(domainCode + ":" + melonBlockCode + "-vine-1-normal"));
            melonBlock = Api.World.GetBlock(new AssetLocation(domainCode + ":" + melonBlockCode + "-fruit-1"));


            stage1VineBlock = api.World.GetBlock(new AssetLocation(domainCode + ":" + melonBlockCode +"-vine-1-normal"));
            melonBlock = api.World.GetBlock(new AssetLocation(domainCode + ":" + melonBlockCode + "-fruit-1"));
            if (api is ICoreServerAPI)
            {
                growListenerId = RegisterGameTickListener(TryGrow, 2000);
            }
        }

        public void CreatedFromParent(BlockPos parentPlantPos, BlockFacing preferredGrowthDir, double currentTotalHours)
        {
            totalHoursForNextStage = currentTotalHours + (double)vineHoursToGrow;
            this.parentPlantPos = parentPlantPos;
            this.preferredGrowthDir = preferredGrowthDir;
        }

        private void TryGrow(float dt)
        {
            if (!DieIfParentDead())
            {
                while (Api.World.Calendar.TotalHours > totalHoursForNextStage)
                {
                    GrowVine();
                    totalHoursForNextStage += vineHoursToGrow;
                }

                TryGrowMelons();
            }
        }

        private void TryGrowMelons()
        {
            BlockFacing[] hORIZONTALS = BlockFacing.HORIZONTALS;
            foreach (BlockFacing blockFacing in hORIZONTALS)
            {
                double num = melonTotalHoursForNextStage[blockFacing];
                while (num > 0.0 && Api.World.Calendar.TotalHours > num)
                {
                    BlockPos blockPos = Pos.AddCopy(blockFacing);
                    Block block = Api.World.BlockAccessor.GetBlock(blockPos);
                    if (IsMelon(block))
                    {
                        int num2 = CurrentMelonStage(block);
                        if (num2 == 4)
                        {
                            num = 0.0;
                        }
                        else
                        {
                            SetMelonStage(block, blockPos, num2 + 1);
                            num += (double)melonHoursToGrow;
                        }
                    }
                    else
                    {
                        num = 0.0;
                    }

                    melonTotalHoursForNextStage[blockFacing] = num;
                }
            }
        }

        private void GrowVine()
        {
            internalStage++;
            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            int num = CurrentVineStage(block);
            if (internalStage > 6)
            {
                SetVineStage(block, num + 1);
            }

            if (IsBlooming())
            {
                if (melonGrowthTries >= maxAllowedMelonGrowthTries || Api.World.Rand.NextDouble() < (double)debloomProbability)
                {
                    melonGrowthTries = 0;
                    SetVineStage(block, 3);
                }
                else
                {
                    melonGrowthTries++;
                    TrySpawnMelon(totalHoursForNextStage - (double)vineHoursToGrow);
                }
            }

            if (num == 3)
            {
                if (canBloom && Api.World.Rand.NextDouble() < (double)bloomProbability)
                {
                    SetBloomingStage(block);
                }

                canBloom = false;
            }

            if (num == 2)
            {
                if (Api.World.Rand.NextDouble() < (double)vineSpawnProbability)
                {
                    TrySpawnNewVine();
                }

                totalHoursForNextStage += vineHoursToGrowStage2;
                canBloom = true;
                SetVineStage(block, num + 1);
            }

            if (num < 2)
            {
                SetVineStage(block, num + 1);
            }
        }

        private void TrySpawnMelon(double curTotalHours)
        {
            BlockFacing[] hORIZONTALS = BlockFacing.HORIZONTALS;
            foreach (BlockFacing blockFacing in hORIZONTALS)
            {
                BlockPos blockPos = Pos.AddCopy(blockFacing);
                Block block = Api.World.BlockAccessor.GetBlock(blockPos);
                if (CanReplace(block) && MelonCropBehavior.CanSupportMelon(Api, blockPos.DownCopy()))
                {
                    Api.World.BlockAccessor.SetBlock(melonBlock.BlockId, blockPos);
                    melonTotalHoursForNextStage[blockFacing] = curTotalHours + (double)melonHoursToGrow;
                    break;
                }
            }
        }

        private bool IsMelon(Block block)
        {
            return block?.Code.GetName().StartsWithOrdinal(melonBlockCode + "-fruit") ?? false;
        }

        private bool DieIfParentDead()
        {
            if (parentPlantPos == null)
            {
                Die();
                return true;
            }

            Block block = Api.World.BlockAccessor.GetBlock(parentPlantPos);
            if (!IsValidParentBlock(block) && Api.World.BlockAccessor.GetChunkAtBlockPos(parentPlantPos) != null)
            {
                Die();
                return true;
            }

            return false;
        }

        private void Die()
        {
            UnregisterGameTickListener(growListenerId);
            growListenerId = 0L;
            Api.World.BlockAccessor.SetBlock(0, Pos);
        }

        private bool IsValidParentBlock(Block parentBlock)
        {
            if (parentBlock != null)
            {
                string name = parentBlock.Code.GetName();
                if (name.StartsWithOrdinal("crop-"+melonBlockCode) || name.StartsWithOrdinal(melonBlockCode+"-vine"))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsBlooming()
        {
            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            block.LastCodePart();
            return block.LastCodePart() == "blooming";
        }

        private bool CanReplace(Block block)
        {
            if (block != null)
            {
                if (block.Replaceable >= 6000)
                {
                    return !block.Code.GetName().Contains(melonBlockCode);
                }

                return false;
            }

            return true;
        }

        private void SetVineStage(Block block, int toStage)
        {
            try
            {
                ReplaceSelf(block.CodeWithParts(toStage.ToString() ?? "", (toStage == 4) ? "withered" : "normal"));
            }
            catch (Exception)
            {
                Api.World.BlockAccessor.SetBlock(0, Pos);
            }
        }

        private void SetMelonStage(Block melonBlock, BlockPos melonPos, int toStage)
        {
            Block block = Api.World.GetBlock(melonBlock.CodeWithParts(toStage.ToString() ?? ""));
            if (block != null)
            {
                Api.World.BlockAccessor.ExchangeBlock(block.BlockId, melonPos);
            }
        }

        private void SetBloomingStage(Block block)
        {
            ReplaceSelf(block.CodeWithParts("blooming"));
        }

        private void ReplaceSelf(AssetLocation blockCode)
        {
            Block block = Api.World.GetBlock(blockCode);
            if (block != null)
            {
                Api.World.BlockAccessor.ExchangeBlock(block.BlockId, Pos);
            }
        }

        private void TrySpawnNewVine()
        {
            BlockFacing vineSpawnDirection = GetVineSpawnDirection();
            BlockPos blockPos = Pos.AddCopy(vineSpawnDirection);
            Block block = Api.World.BlockAccessor.GetBlock(blockPos);
            if (!IsReplaceable(block))
            {
                return;
            }

            blockPos.Y--;
            if (CanGrowOn(Api, blockPos))
            {
                blockPos.Y++;
                Api.World.BlockAccessor.SetBlock(stage1VineBlock.BlockId, blockPos);
                if (Api.World.BlockAccessor.GetBlockEntity(blockPos) is BEMelonVine blockEntityMelonVine)
                {
                    blockEntityMelonVine.CreatedFromParent(Pos, vineSpawnDirection, totalHoursForNextStage);
                }
            }
        }

        private bool CanGrowOn(ICoreAPI api, BlockPos pos)
        {
            return api.World.BlockAccessor.GetMostSolidBlock(pos).CanAttachBlockAt(api.World.BlockAccessor, stage1VineBlock, pos, BlockFacing.UP);
        }

        private bool IsReplaceable(Block block)
        {
            if (block != null)
            {
                return block.Replaceable >= 6000;
            }

            return true;
        }

        private BlockFacing GetVineSpawnDirection()
        {
            if (Api.World.Rand.NextDouble() < (double)preferredGrowthDirProbability)
            {
                return preferredGrowthDir;
            }

            return DirectionAdjacentToPreferred();
        }

        private BlockFacing DirectionAdjacentToPreferred()
        {
            if (BlockFacing.NORTH == preferredGrowthDir || BlockFacing.SOUTH == preferredGrowthDir)
            {
                if (!(Api.World.Rand.NextDouble() < 0.5))
                {
                    return BlockFacing.WEST;
                }

                return BlockFacing.EAST;
            }

            if (!(Api.World.Rand.NextDouble() < 0.5))
            {
                return BlockFacing.SOUTH;
            }

            return BlockFacing.NORTH;
        }

        private int CurrentVineStage(Block block)
        {
            int.TryParse(block.LastCodePart(1), out var result);
            return result;
        }

        private int CurrentMelonStage(Block block)
        {
            int.TryParse(block.LastCodePart(), out var result);
            return result;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            totalHoursForNextStage = tree.GetDouble("totalHoursForNextStage");
            canBloom = tree.GetInt("canBloom") > 0;
            BlockFacing[] hORIZONTALS = BlockFacing.HORIZONTALS;
            foreach (BlockFacing blockFacing in hORIZONTALS)
            {
                melonTotalHoursForNextStage[blockFacing] = tree.GetDouble(blockFacing.Code);
            }

            melonGrowthTries = tree.GetInt("melonGrowthTries");
            parentPlantPos = new BlockPos(tree.GetInt("parentPlantPosX"), tree.GetInt("parentPlantPosY"), tree.GetInt("parentPlantPosZ"));
            preferredGrowthDir = BlockFacing.ALLFACES[tree.GetInt("preferredGrowthDir")];
            internalStage = tree.GetInt("internalStage");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("totalHoursForNextStage", totalHoursForNextStage);
            tree.SetInt("canBloom", canBloom ? 1 : 0);
            BlockFacing[] hORIZONTALS = BlockFacing.HORIZONTALS;
            foreach (BlockFacing blockFacing in hORIZONTALS)
            {
                tree.SetDouble(blockFacing.Code, melonTotalHoursForNextStage[blockFacing]);
            }

            tree.SetInt("melonGrowthTries", melonGrowthTries);
            if (parentPlantPos != null)
            {
                tree.SetInt("parentPlantPosX", parentPlantPos.X);
                tree.SetInt("parentPlantPosY", parentPlantPos.Y);
                tree.SetInt("parentPlantPosZ", parentPlantPos.Z);
            }

            if (preferredGrowthDir != null)
            {
                tree.SetInt("preferredGrowthDir", preferredGrowthDir.Index);
            }

            tree.SetInt("internalStage", internalStage);
        }
    }
}
