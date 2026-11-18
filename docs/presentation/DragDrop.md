# Avalonia 12 拖放实现指南

> 本文档记录了 SeatFlow 座位安排页面（`SeatingArrangementView`）的拖放实现模式和踩坑记录。
> Avalonia 版本：12.0.4

## 核心 API

### 发起拖放

唯一正确方式：在 `PointerPressed` 处理器中调用 `DragDrop.DoDragDropAsync`。

```csharp
private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
{
    if (!ShouldDrag()) return;

    var data = new DataTransfer();
    var item = new DataTransferItem();
    item.Set(format, value);
    data.Add(item);

    var result = await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);

    // result != None → 拖放完成，不触发点击
    // result == None → 用户没有拖动，当作点击处理
    if (result != DragDropEffects.None) return;

    HandleClick();
}
```

**关键点：**
- `DoDragDropAsync` 是阻塞异步方法 — 用户松开鼠标或取消拖放后才返回
- 返回值区分拖放（`Move`/`Copy`）和点击（`None`），无需手动阈值检测
- **不要在 `PointerMoved` 中调用** — 事件已过按压阶段，内部手势检测无法工作

### 接收放置

目标元素**必须**设置 `DragDrop.AllowDrop="True"`，否则拖放事件（`DragOver`/`Drop`）根本不会传递到该元素。

```xml
<Border DragDrop.AllowDrop="True"
        DragDrop.DragOver="OnDragOver"
        DragDrop.Drop="OnDrop"
        DragDrop.DragLeave="OnDragLeave">
```

### 自定义数据格式

使用 `DataFormat.CreateInProcessFormat<T>` 创建进程内格式（同应用内拖放，不序列化出进程）：

```csharp
internal static class DragFormats
{
    public static readonly DataFormat<string> StudentDrag =
        DataFormat.CreateInProcessFormat<string>("SeatFlow_Student");
    public static readonly DataFormat<string> SeatDrag =
        DataFormat.CreateInProcessFormat<string>("SeatFlow_Seat");
}
```

| 方法 | 说明 |
|------|------|
| `CreateInProcessFormat<T>("name")` | 进程内格式，同应用内传输引用，性能最优 |
| `CreateStringApplicationFormat("app.name")` | 应用程序格式，可跨进程（但格式名需包含 `.`） |

## 数据读写

### 写入（发起方）

```csharp
var data = new DataTransfer();
var item = new DataTransferItem();
item.Set(DragFormats.StudentDrag, studentId);   // DataFormat<string>, string?
data.Add(item);
```

:::warning
`DataTransfer.Set()` 方法在 Avalonia 12.0.4 中**不存在**（文档中有但实际缺失）。必须用 `DataTransferItem`。
:::

### 读取（接收方）

```csharp
// 检查格式是否存在
private static bool DragHasFormat(IDataTransfer transfer, DataFormat format)
    => transfer.Items.Any(item => item.Formats.Contains(format));

// 读取字符串数据
private static string? DragGetString(IDataTransfer transfer, DataFormat<string> format)
{
    foreach (var item in transfer.Items)
    {
        if (item.Formats.Contains(format))
            return item.TryGetRaw(format) is string s ? s : null;
    }
    return null;
}
```

:::warning
`TryGetRaw(DataFormat)` 返回 `object?`，必须用 `is string s` 模式匹配解包。泛型重载 `TryGetRaw<T>(DataFormat<T>)` 不存在于 12.0.4。
:::

## 座位安排页面的交互模式

### 交互矩阵

| 操作 | 触发 | 数据 | 命令 |
|------|------|------|------|
| 点击已占座位 → 交换模式 | `PointerPressed` → click（`result == None`） | — | `ClickSeatCommand` |
| 拖动已占座位 → 空座位 | `PointerPressed` → drag | `StudentDrag` + `SeatDrag` | `SwapSeatCommand(dst=null)` |
| 拖动已占座位 → 已占座位 | `PointerPressed` → drag | `StudentDrag` + `SeatDrag` | `SwapSeatCommand` |
| 拖动列表学生 → 空座位 | `PointerPressed` → drag | `StudentDrag` | `AssignSeatCommand` |
| 拖动已占座位 → 垃圾桶 | `PointerPressed` → drag + `Trash_Drop` | `SeatDrag` | `RemoveStudentCommand` |
| 点击垃圾桶 | `Trash_PointerPressed` | — | `RemoveStudentCommand` |

### 座位 DragOver 验证逻辑

```csharp
private void Seat_DragOver(object? sender, DragEventArgs e)
{
    // 1. 先验证 sender 是座位 Border（不要在此之前设置 e.Handled）
    if (sender is not Border || DataContext is not SeatDisplayItem seat)
        return;  // 让事件继续冒泡

    // 2. 固定座位拒绝
    if (seat.IsFixed) { e.DragEffects = DragDropEffects.None; return; }

    // 3. 已占且不是从座位拖来的 → 拒绝（防止从未分配列表拖到已占位）
    if (seat.IsOccupied && !DragHasFormat(e.DataTransfer, DragFormats.SeatDrag))
    { e.DragEffects = DragDropEffects.None; return; }

    // 4. 不能拖到自己
    if (srcSeatId == seat.SeatId) { e.DragEffects = DragDropEffects.None; return; }

    // 5. 所有验证通过后设置"接受"状态
    seat.IsDragHover = true;
    e.DragEffects = DragDropEffects.Move;
    e.Handled = true;  // ← 最后才设置！
}
```

## 画布缩放/平移交互（M5 起内置到 SeatingCanvas）

> 历史说明：M0–M4 期间平移/缩放由 `Behaviors/CanvasZoomPan` + `Behaviors/ZoomOnScroll` 附加行为实现；
> M1 起 `SeatingCanvas` 内置矩阵变换（Ctrl+滚轮缩放、空白拖拽平移），M5 已删除这两个行为文件。
> 下方 NaN 哨兵方案是行为时代的实现记录，保留供理解「拖放与指针事件冲突」的解决思路。

拖放与画布缩放/平移共用指针事件。冲突解决：

### 座位 Border 上的按压

1. `SeatBorder_PointerPressed` → 调用 `DoDragDropAsync`
2. `DoDragDropAsync` 内部消费指针事件 → `PointerPressed` 不再冒泡到 `ScrollViewer`
3. `CanvasZoomPan.OnPointerPressed` 不会收到事件

**兜底保护**（空座位等不调用 `DoDragDropAsync` 的情况）：

```csharp
// CanvasZoomPan.OnPointerPressed
if (e.Source is StyledElement src && src.DataContext is SeatDisplayItem)
{
    sv.SetValue(PanOriginProperty, new Point(double.NaN, double.NaN));
    return;  // 跳过平移
}

// CanvasZoomPan.OnPointerMoved
if (double.IsNaN(origin.X)) return;  // 哨兵检查，防止默认 (0,0) 触发平移
```

## 触摸与手写笔（触屏假死防护）

系统 DnD 与自绘手势在触摸端行为不同，必须显式区分指针类型：

| 事实 | 影响 |
|------|------|
| 触摸/手写笔没有「左键」概念，`PointerPointProperties.IsLeftButtonPressed` 恒为 `false` | 只用 `IsLeftButtonPressed` 判定会让触屏上「点座位 / 拖座位 / 平移」全部失效 |
| `e.Pointer.Type` 为 `PointerType.Touch` / `PointerType.Pen` | 判定改为「`isTouch` 直接放行，非触摸才检查左键」 |
| 触摸按下后指针仍被列表捕获时，`DragOver`/`Drop` 不触发，`DoDragDropAsync` 的 `await` 永不返回 | **在 `PointerPressed` 里对触摸调用系统 DnD 会让列表与页面假死** |
| Avalonia 12 没有 `OnPointerCancel` / `PointerCancelEventArgs`，但有 `OnPointerCaptureLost` 与 `Gestures.Holding` | 手势取消靠捕获丢失兜底；长按用 `Holding` 路由事件 |

`SeatingCanvas` 的状态机约定：

```csharp
// 1) 放行触摸/手写笔
var isTouch = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
if (!isTouch && !props.IsLeftButtonPressed) return;

// 2) 单指针：已在处理时忽略第二根手指，且残留状态先清掉
if (_activePointer is not null && !ReferenceEquals(_activePointer, e.Pointer)) return;
if (_activePointer is not null) ResetPointerState();

// 3) 结束手势统一走 CompleteGesture，finally 里无条件 ResetPointerState()
// 4) 捕获丢失 = 放弃手势，绝不能让 _dragSeatId 残留
protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { ... }
```

未分配学生列表的触摸路径（`SeatingArrangementView.axaml.cs`）：

- **鼠标**：`DoDragDropAsync` 系统 DnD（保持原有体验）
- **触摸/手写笔**：按下只做选中（`SelectedUnassignedStudent = student`），随后点座位即放置；
  或在画布上长按（`Holding`，`HoldingState.Started`）把选中学生放入长按命中的空座位
- 触摸端不设置鼠标游标（`Cursor`），避免残留拖动形鼠标指针

### 双指缩放（捏合）

触摸设备没有滚轮，`SeatingCanvas` 自己跟踪两根 Touch 指针做捏合缩放（Avalonia 12.1.6 没有内置 pinch
识别器，`GestureRecognizers` 也没有可用的缩放识别器）。要点：

| 约定 | 原因 |
|------|------|
| **每根手指各记各的位置**（`_pinchFirstPosition` / `_pinchSecondPosition`） | 早期实现两根手指共用 `_pointerPosition`，第一根手指一移动就把第二根的位置覆盖成自己的，两指间距瞬间归零 → 缩放掉到 `MinZoom` 再弹回 `MaxZoom`，表现为**会场抽搐、甚至缩到看不见** |
| `OnPointerMoved` 里双指分支必须写在 `_pointerPosition = e.GetPosition(this)` **之前** | 同上：先写单指位置再算捏合，等于每帧都把另一根手指的位置污染一次 |
| 缩放用「起点缩放 × 间距比」的**绝对值**映射，不用逐帧增量 | 增量累积撞到上下限后会卡住，手指合拢时回不来 |
| 间距 < `MinPinchSpan`（12px）时重新取基线，绝不拿接近 0 的比值去缩放 | 两指重合或丢帧时比值失真 |
| 第三根手指不参与（`_pinchGestureSeen` 期间不再重启基线） | 否则捏合被「重启」，按错的两根手指算间距，画面同样跳 |
| 抬起任一参与手指 → `CancelPinch()`，剩余手指不再被当成捏合 | 否则剩下的那根手指会带着过期基线继续缩放/平移 |

```csharp
protected override void OnPointerMoved(PointerEventArgs e)
{
    base.OnPointerMoved(e);
    if (_pinchGestureSeen)          // 先处理捏合
    {
        UpdatePinch(e.Pointer, e.GetPosition(this));
        return;
    }
    _pointerPosition = e.GetPosition(this);   // 单指状态才写这个字段
    ...
}

private void UpdatePinch(IPointer pointer, Point position)
{
    if (ReferenceEquals(pointer, _pinchFirstPointer)) _pinchFirstPosition = position;
    else if (ReferenceEquals(pointer, _pinchSecondPointer)) _pinchSecondPosition = position;
    else return;                     // 第三根手指 / 残留移动不参与

    var span = Distance(_pinchFirstPosition, _pinchSecondPosition);
    if (span < MinPinchSpan || _pinchStartSpan < MinPinchSpan) { 重新取基线; return; }

    var target = Math.Clamp(_pinchStartZoom * (span / _pinchStartSpan), MinZoom, MaxZoom);
    var delta = target / Zoom;
    if (Math.Abs(delta - 1) > 1e-4) ZoomAt(delta, Midpoint(...));
    PanBy(center - _pinchLastCenter);  // 双指整体平移
}
```

回归测试在 `tests/SeatFlow.Presentation.Tests/SeatingCanvasTests.cs` 的「双指缩放稳定性」段
（用 `window.TouchBegin/TouchMove/TouchEnd` 注入真实触摸指针）：间距翻倍时缩放必须精确翻倍、
交替张开必须单调、锚点下的座位不能漂走、撞上限后合拢必须立刻回落、第三根手指与松开一指都不影响缩放。

### 平移边界与视图重置

捏合缩放会把板面放大到超出视口。如果平移量不设上限，用户可以把整块板面推出视口，之后既看不到会场、
也没有「回到查看范围」的入口。`SeatingCanvas` 在属性层统一收敛（外部代码、手势、按钮都绕不过去）：

| 约定 | 原因 |
|------|------|
| `PanOffset` 的 setter 与 `Zoom` 的 setter 都经过 `ClampPan` | 任何写入路径（拖拽、滚轮、捏合、按钮、绑定）都不能把板面推丢 |
| 每个方向至少保留 `MinVisibleViewportFraction`（0.75）的视口可见；板面比这个阈值小时整块保留 | 既保证「拖不丢」，又不会把捏合锚点强行拉回中心（否则手指下方的座位会漂走） |
| 边界是「板面尺寸 × 缩放」的连续函数 | 早期按「居中/贴边」两分支会让缩放跨过临界点时偏移量突变，画面跳一下 |
| `ResetView(zoom)` 同时把 `PanOffset` 归零 | 只重置缩放会把旧平移量留下，缩小后会场仍可能停在视口外（点击右下角百分比即为重置） |
| `ArrangeOverride` 中再次 `ClampPan` | 窗口尺寸变化后旧的平移量可能已越界 |
| 快照尺寸变化（换会场）→ 平移归零；同尺寸快照替换（每次落座重建）→ 收敛到合法范围 | 换会场应回到初始视图，落座重建不应让视图跳动 |
| `TryStartPinch` 优先于单指拖拽：第二根手指落下即取消拖拽/平移 | 触摸抖动 4px 就会进入拖拽状态，旧逻辑会拒绝捏合，表现为缩放间歇失效、比例不对 |

回归测试同在 `SeatingCanvasTests.cs` 的「缩放/平移边界」段：平移超界后板面仍留在视口内、放大后拖到极限
仍保留 75% 视口、缩小后平移量被收敛、重置缩放同时清空平移、第一根手指已拖动时第二根手指仍能捏合。

## 常见坑

| 问题 | 现象 | 原因 | 修复 |
|------|------|------|------|
| 拖不动 | 按下拖动无反应 | 使用 `PointerMoved` 启动拖放 | 改用 `PointerPressed` + `DoDragDropAsync` |
| 触屏假死（列表） | 触摸点一下未分配学生后整页无响应 | 触摸端 `DoDragDropAsync` 的 `await` 永不返回 | 触摸不走系统 DnD，改点选 + 长按放置 |
| 触屏假死（画布） | 座位拖到一半松手后画布不再响应点击 | 捕获丢失未清 `_dragSeatId`，画布永久处于拖动态 | 重写 `OnPointerCaptureLost` + `ResetPointerState()` |
| 触屏点座位没反应 | 点击/拖动/平移全部失效 | 用 `IsLeftButtonPressed` 判定 | 按 `e.Pointer.Type` 放行触摸与手写笔 |
| 双指缩放抽搐/会场消失 | 捏合时画面来回跳，最后缩到看不见 | 两根手指共用 `_pointerPosition`，第一根移动已把第二根位置覆盖 → 间距算成 0 | 两指各记各的位置，捏合分支放在写 `_pointerPosition` 之前，缩放走绝对值映射 |
| 缩放后会场找不回 | 捏合/拖动后整块板面跑到视口外，怎么拖都不回来 | `PanOffset` 无边界约束，缩放后旧平移量仍然生效 | `Zoom`/`PanOffset` setter 统一 `ClampPan`，每方向至少保留 75% 视口；重置缩放同时清空平移 |
| 捏合时好时坏 | 双指缩放偶尔完全不动，像没识别到 | 第一根手指抖动 >4px 先进入拖拽，`TryStartPinch` 因「正在拖动」拒绝捏合 | 捏合优先接管并取消单指拖拽/平移；非当前手势指针的移动不写单指状态 |
| 禁止光标 | 鼠标显示禁止符号 | 目标缺少 `AllowDrop="True"`，或 `e.Handled=true` 过早 + `DragEffects` 保持 `None` | 加 `AllowDrop`，`e.Handled` 移到验证后 |
| 拖不到垃圾桶 | 拖到垃圾桶上无反应 | 垃圾桶缺少 `AllowDrop="True"` | 加 `AllowDrop` |
| 拖动时画布乱移 | 座位拖动时画布同时平移 | 平移与座位拖放未隔离（行为时代 `CanvasZoomPan` 误触发） | M5 起由 `SeatingCanvas` 内部手势状态机隔离（历史方案：NaN 哨兵 + `OnPointerMoved` 检查） |
| `DataTransfer.Set()` 不存在 | CS1929 | 文档和实际 API 版本不一致 | 用 `DataTransferItem.Set()` + `DataTransfer.Add()` |
| `TryGetRaw` 返回 `object?` | 类型不匹配 | 泛型重载不存在 | 用 `is string s` 模式匹配 |
| `e.Handled` 导致子元素事件丢失 | 点击不触发点击命令 | `PointerPressed` 中设置 `e.Handled = true` 阻止事件冒泡 | 不设置 `e.Handled`，让系统处理 |
