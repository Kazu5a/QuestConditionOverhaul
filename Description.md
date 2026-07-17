# Kill & Bill - Quest Completion Overhaul {.tabset}

Tarkov no longer needs excuses.
Traders no longer care how the job gets done. They only care about the result.

In the end, every quest comes down to three things:

**Kill the target. Bring back proof. Pay the bill.**

Kill & Bill rewrites quest completion requirements into a more direct, mercenary-style system: killing targets, turning in PMC dogtags, or paying money.

No more running in circles.
No more checklist busywork.
The traders want results, proof, or cash.

## Overview {.tabset}

Kill & Bill is a quest completion condition overhaul mod for SPT 4.0.

It does not add new quests, change quest chains, reassign traders, or modify quest rewards. Instead, it rewrites the completion conditions of already loaded quests and replaces them with one of three objective types:

* Kill targets
* Turn in PMC dogtags
* Pay money

Quest IDs, quest chains, unlock order, traders, and rewards are preserved.

## Features

* Supports vanilla quests and other modded quests loaded before this mod
* Rewrites `AvailableForFinish` quest completion conditions
* Preserves original quest IDs, quest chains, traders, and rewards
* Does not modify `AvailableForStart` start conditions
* Supports deterministic generation through a configurable seed
* Same seed and same config will generate the same quest completion conditions
* Configurable objective weights for kill, dogtag, and money objectives
* Automatically rewrites quest objective text to match the new completion conditions

## Objective Types

### Kill

Quests can be converted into kill objectives.
Kill count and target type weights can be adjusted in the config file.

### Dogtags

Some quests require PMC dogtags as proof that the target has been dealt with.

You can configure:

* Whether dogtags must be found in raid
* Whether to use additional dogtags from WTT - Content Backport
* Required dogtag turn-in count

Technically, you can also replace the required item with something else by changing the item ID.

### Money

Some jobs do not need bullets.
They just need the bill paid.

Money turn-in ranges can be adjusted in the config file.

## Configuration

The mod includes a `config.jsonc` file.

You can configure:

* Whether the mod is enabled
* Whether to process all loaded quests
* Included and excluded traders
* Excluded quests
* Randomization seed
* Objective weights for kill, dogtag, and money objectives
* Kill objective count ranges
* PMC / SCAV target weights
* Dogtag turn-in requirements
* Money turn-in ranges
* Quest objective text templates

Changing the seed or config will generate a different set of quest completion conditions.

## Installation

1. Download the release archive.
2. Extract it into your SPT root directory.
3. Make sure the mod folder is located at:

`SPT/user/mods/kazusa-QuestConditionOverhaul`

4. Start SPT Server.

## Compatibility

Kill & Bill modifies already loaded quest completion conditions during the `PostDBLoad` stage.

It does not create new quests, reassign traders, rewrite quest chains, or modify quest rewards.

In theory, it should work with most loaded quests from modded traders.

If you use other quest overhaul mods that add, remove, or rewrite `AvailableForFinish` conditions, their functionality may overlap. The final result usually depends on which mod applies its changes last.

For the best experience, it is not recommended to use other quest completion randomizer mods at the same time. Mods that only add quests, modify rewards, or adjust start conditions are generally easier to keep compatible.

{.endtabset}

# Kill & Bill - 任务完成条件重制

塔科夫不再需要借口。
商人们不再关心过程，他们只看结果。

任务最终只剩下三件事：

**杀掉目标、带回证据、付清账单。**

Kill & Bill 会将任务完成条件重写为更直接、更雇佣兵化的目标系统：击杀、提交 PMC 狗牌、支付金钱。

不再绕流程。
不再跑清单。
商人要的只有结果、证据，或者钱。

## 模组介绍 {.tabset}

Kill & Bill 是一个面向 SPT 4.0 的任务完成条件重制模组。

它不会添加新任务，也不会改变任务链、商人归属或任务奖励。模组只会重写已加载任务的完成条件，并将其替换为以下三类目标之一：

* 击杀目标
* 提交 PMC 狗牌
* 支付金钱

任务 ID、任务链、解锁顺序、所属商人和奖励都会保留。

## 主要功能

* 支持原版任务，以及在本模组之前加载的其他模组任务
* 重写任务的 `AvailableForFinish` 完成条件
* 保留原始任务 ID、任务链、商人和奖励
* 不修改 `AvailableForStart` 开始条件
* 支持通过配置种子进行确定性生成
* 相同种子和相同配置会生成相同的任务完成条件
* 可配置击杀、狗牌、金钱三类目标的生成权重
* 自动重写任务目标文本，使其匹配新的完成条件

## 三类目标

### 击杀

任务会被转换为击杀目标。
击杀数量和目标类型权重可在配置文件中调整。

### 狗牌

任务会要求提交 PMC 狗牌，作为目标已被处理的证明。

你可以在配置中调整：

* 狗牌是否必须来自战局
* 是否使用 WTT - Content Backport 新增的狗牌
* 狗牌提交数量

理论上，也可以通过修改物品 ID 将提交目标替换为其他物品。

### 金钱

有些任务不需要子弹。
只需要付清账单。

金钱提交范围可在配置文件中调整。

## 配置

模组包含 `config.jsonc` 配置文件。

你可以调整：

* 是否启用模组
* 是否处理所有已加载任务
* 指定包含和排除的商人
* 指定排除的任务
* 随机化种子
* 三类目标的生成权重
* 击杀目标数量范围
* PMC / SCAV 目标权重
* 狗牌提交需求
* 金钱提交范围
* 任务目标文本模板

修改种子或配置后，会生成另一套任务完成条件。

## 安装方式

1. 下载 release 压缩包。
2. 解压到 SPT 根目录。
3. 确保模组文件夹位于：

`SPT/user/mods/kazusa-QuestConditionOverhaul`

4. 启动 SPT Server。

## 兼容性说明

Kill & Bill 会在 `PostDBLoad` 阶段修改已经加载的任务完成条件。

它不会生成新任务，不会重新分配商人，不会重写任务链，也不会修改任务奖励。

理论上，它可以作用于大多数已加载的模组商人任务。

如果同时使用其他会添加、删除或重写 `AvailableForFinish` 条件的任务重制模组，可能会出现功能重叠。最终效果通常取决于哪个模组最后应用修改。

为了获得最佳体验，不建议同时启用其他任务完成条件随机化模组。只添加任务、修改奖励或调整开始条件的模组通常更容易兼容。
{.endtabset}
{.endtabset}