using System.Collections.Generic;

namespace Factory.Simulation
{
    // 채굴기들을 매 틱 순회하며 산출을 누적하고, 벨트 없이 곧바로 코어 저장고로 "원격 전송"한다.
    // 채굴기는 입출력 포트가 없다 — 캔 만큼 즉시 코어로 들어간다는 게 원래 기획 의도.
    // 코어가 가득 차 있으면 BufferedOutput에 대기시켰다가 다음 틱에 다시 시도한다(잃지 않음).
    public sealed class MinerSystem
    {
        public void Tick(float deltaSeconds, List<MinerInstance> miners, List<ProcessorInstance> processors, int coreProcessorIndex)
        {
            ProcessorInstance core = coreProcessorIndex >= 0 && coreProcessorIndex < processors.Count
                ? processors[coreProcessorIndex]
                : null;

            for (int i = 0; i < miners.Count; i++)
            {
                var miner = miners[i];
                if (miner == null) continue; // 철거로 비워진 슬롯(SimulationWorld.RemoveMiner 참고).

                miner.Progress += deltaSeconds * miner.SpeedMultiplier;

                while (miner.Progress >= miner.MineIntervalSeconds)
                {
                    miner.Progress -= miner.MineIntervalSeconds;
                    miner.BufferedOutput = System.Math.Min(
                        miner.BufferedOutput + miner.YieldPerCycle,
                        SimulationConstants.ResourceBufferCapacity);
                }

                if (core == null) continue;

                while (miner.BufferedOutput > 0 && core.TryAcceptInput(miner.OutputResourceId, 1))
                {
                    miner.BufferedOutput--;
                }
            }
        }
    }
}
