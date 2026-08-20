using System.Collections.Generic;

namespace Factory.Simulation
{
    // 채굴기들을 매 틱 순회하며 산출을 누적한다. 벨트로 실어 나르는 건 BeltSystem이
    // BufferedOutput을 읽어가는 방식으로 처리 — MinerSystem은 벨트 존재 여부를 몰라도 된다.
    public sealed class MinerSystem
    {
        public void Tick(float deltaSeconds, List<MinerInstance> miners)
        {
            for (int i = 0; i < miners.Count; i++)
            {
                var miner = miners[i];
                miner.Progress += deltaSeconds * miner.SpeedMultiplier;

                while (miner.Progress >= miner.MineIntervalSeconds)
                {
                    miner.Progress -= miner.MineIntervalSeconds;
                    if (miner.BufferedOutput < SimulationConstants.ResourceBufferCapacity)
                    {
                        miner.BufferedOutput++;
                    }
                }
            }
        }
    }
}
