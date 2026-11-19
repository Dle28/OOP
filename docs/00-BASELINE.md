# OS Simulation Studio — Baseline v1.2

>Tài liệu này là nguồn chuẩn duy nhất về phạm vi, kiến trúc, quy tắc nghiệp vụ, hợp đồng và quyền sở hữu class/API. Đọc file này trước khi sửa mã. Tài liệu và UML phải được cập nhật trong cùng thay đổi với mã làm thay đổi hợp đồng hoặc hành vi.

## 1. Phạm vi và quyết định kỹ thuật

| Quyết định | Chuẩn hiện tại |
|---|---|
| Mục tiêu | Ứng dụng học tập mô phỏng lập lịch CPU, đồng bộ tiến trình, cấp phát tài nguyên Banker và bộ nhớ ảo. |
| Nền tảng | C# WPF, Windows, `net10.0-windows`; solution `OsSim.slnx`. |
| SDK | `global.json` pin .NET SDK `10.0.404`, cho phép roll-forward trong feature band. |
| Kiến trúc | Modular monolith gồm `OsSim.Domain`, `OsSim.Application`, `OsSim.Wpf`; kiểm thử Domain và Application. |
| UI | MVVM với CommunityToolkit.Mvvm 8.4.0; resource dictionary của WPF giữ palette/control style; không chứa nghiệp vụ mô phỏng trong code-behind. |
| Trạng thái | Trong bộ nhớ; chưa có lưu/mở kịch bản, cơ sở dữ liệu hay dịch vụ mạng. |
| Thời gian | Thời gian ảo, có thể lặp lại; actor mô phỏng không phải OS process/thread. |
| Tích hợp | Không điều khiển tiến trình thật hay truy cập bộ nhớ thật của hệ điều hành. |

## 2. Quy tắc đọc trước và quản lý thay đổi

1. Đọc tài liệu này trước khi sửa mã; `README.md` và `AGENTS.md` dẫn tới quy tắc này.
2. Tìm class/API hiện có trong bảng trách nhiệm và trong solution trước khi tạo thành phần mới.
3. Mỗi nghiệp vụ có một chủ sở hữu. Hợp đồng chung thuộc `Domain/Simulation`; luật riêng giữ trong module tương ứng.
4. Cập nhật tài liệu chuẩn, API registry và sơ đồ UML liên quan trong cùng thay đổi với hợp đồng, phụ thuộc, class ownership, thuật toán hoặc luồng UI.
5. Trạng thái API: `Planned` là thiết kế chưa có mã; `Implemented` có mã tương ứng; `Deprecated` không dùng cho mã mới và cần kế hoạch di chuyển. Không trình bày API Planned như thể đã chạy được.
6. `Domain` và `Application` không tham chiếu WPF. Timer và hiển thị thuộc `Wpf`.
7. Đầu vào hoặc lệnh không hợp lệ trả lỗi có kiểu và không làm đổi snapshot hiện tại. Exception chỉ dành cho lỗi bất biến nội bộ.
8. Không thêm trạng thái global mutable, randomness ẩn, thread/process thật, cơ sở dữ liệu hoặc một playback controller thứ hai.
9. Thay đổi thứ tự ưu tiên, quy tắc hòa, thứ tự hàng đợi, hoặc cách tính số liệu là thay đổi hợp đồng: cập nhật baseline, kiểm thử và UML liên quan.

## 3. Phân cấp solution, module và class

```text
OsSim.slnx
├── src/OsSim.Domain                         # Không phụ thuộc WPF
│   ├── Simulation                           # Contract, lỗi, event, session
│   ├── Cpu                                  # Policy, lịch chạy, Gantt, metrics
│   ├── Synchronization                      # Actor ảo, tài nguyên, wait graph
│   ├── ResourceAllocation                   # Banker safety algorithm và kiểm tra request
│   └── Memory                               # Dịch địa chỉ, page table, TLB, thay trang
├── src/OsSim.Application                    # Ranh giới use case
│   └── SimulationCatalog
├── src/OsSim.Wpf                            # Composition root, UI, MVVM
│   ├── Views                                # MainWindow shell và 4 màn hình mô phỏng
│   ├── ViewModels                           # Điều hướng shell, từng mô-đun, model hiển thị
│   └── Playback                             # PlaybackController dùng chung
└── tests
    ├── OsSim.Domain.Tests
    └── OsSim.Application.Tests
```

Phụ thuộc được phép: `Wpf → Application → Domain`. Mỗi project kiểm thử tham chiếu project sản phẩm cần kiểm tra. `Domain` không tham chiếu `CommunityToolkit.Mvvm`; package MVVM chỉ ở WPF.

## 4. Bảng mô-đun, thuật toán và hợp đồng vào/ra

| Mô-đun | Thuật toán / kịch bản | Đầu vào chính | Đầu ra chính | Chủ sở hữu |
|---|---|---|---|---|
| Lập lịch CPU | FCFS; SJF không ngắt; SRTF; Priority không ngắt/có ngắt; Round Robin | Danh sách mã tiến trình, thời điểm đến, CPU burst, độ ưu tiên; quantum cho RR; overhead chuyển ngữ cảnh dạng số thập phân | Snapshot theo tick CPU và đoạn context switch, Gantt gồm đoạn rỗi/CS, hàng đợi ready, completion/turnaround/waiting/response, số lần/ tổng overhead chuyển ngữ cảnh | `CpuSchedulingModule`, `ICpuSchedulingPolicy` |
| Đồng bộ tiến trình | Producer–Consumer với buffer hữu hạn, semaphore và mutex; Dining Philosophers kiểu lấy trái trước hoặc phân cấp tài nguyên | Sức chứa; số producer/consumer và số sản phẩm; hoặc số triết gia/bữa; lệnh chọn actor tùy chọn | Trạng thái actor, buffer/tài nguyên/hàng đợi, event, wait-for graph, chu trình deadlock | `SynchronizationModule`, `SynchronizationSnapshot` |
| Cấp phát tài nguyên | Banker safety algorithm; kiểm tra cấp phát request trên trạng thái tạm | Tên tài nguyên, Available, Allocation, Max và request theo tiến trình | Need, Work, Finish, safe sequence hoặc unsafe state; kết quả request Granted/Wait/WouldBeUnsafe | `BankerModule`, `IBankerModule` |
| Bộ nhớ ảo | Paging, dịch địa chỉ, TLB bật/tắt; FIFO, LRU, Optimal, Clock; TLB LRU; swap-in/out các trang bị thay | Chuỗi địa chỉ ảo, số bit địa chỉ, page size, frame count, chính sách, sức chứa TLB | VPN/offset/PFN/địa chỉ vật lý, page table, frame, TLB, swap pages, hit/miss/fault, replacement và swap I/O | `VirtualMemoryModule`, `IPageReplacementPolicy` |

**Quy ước CPU:** số ưu tiên nhỏ hơn chạy trước; hòa được giải quyết ổn định theo thời điểm đến rồi thứ tự nhập. RR nhận tiến trình đến tại thời điểm quantum kết thúc vào ready queue trước khi đưa tiến trình vừa hết lượt về cuối hàng đợi. SRTF và Priority có ngắt chỉ ngắt khi ứng viên mới có khóa ưu tiên tốt hơn.

Overhead được cộng khi CPU chuyển trực tiếp từ một tiến trình sang tiến trình khác; không tính khi vừa hết khoảng CPU rỗi hoặc khi RR chọn lại cùng tiến trình. Tiến trình được chọn tại thời điểm bắt đầu chuyển; tiến trình đến trong khoảng overhead vào ready queue và được xét ở quyết định kế tiếp. Gantt có đoạn `CS` riêng khi overhead > 0. CPU utilization tính cả thời gian overhead là CPU bận; `ContextSwitchCount` vẫn đếm lượt đổi tiến trình khi overhead bằng 0.

**Quy ước đồng bộ:** mô phỏng theo bước nguyên tử của actor ảo. Bước tay chọn actor Ready; tự phát chọn actor Ready tiếp theo theo vòng tròn xác định. Hàng đợi resource đánh thức FIFO. Kịch bản deadlock khi còn việc nhưng không actor nào Ready; snapshot báo cả cạnh chờ và chu trình nếu có.

**Quy ước bộ nhớ:** địa chỉ hợp lệ được tách thành VPN và offset; TLB hit, page-table hit và page fault là ba kết quả riêng. Khi thay trang, page table và mục TLB cũ cùng bị xóa; trang bị thay được đưa vào danh sách swap, truy cập lại sẽ swap-in trang đó. Mô hình swap theo dõi trang và lượt I/O, không mô phỏng nội dung byte, ghi trang hay dirty bit. Clock đặt bit tham chiếu khi truy cập, quét từ clock hand và bỏ bit đã đặt trước khi chọn victim. Optimal dùng toàn bộ chuỗi tham chiếu tương lai và là chính sách so sánh offline.

**Quy ước Banker:** tại mỗi bước chọn tiến trình chưa Finish đầu tiên có `Need ≤ Work`, đánh dấu hoàn tất rồi cộng Allocation vào Work. Nếu không tìm được tiến trình nhưng vẫn còn tiến trình chưa Finish, snapshot kết thúc ở `Unsafe`. Request được kiểm tra theo thứ tự `Request ≤ Need`, `Request ≤ Available`, rồi chạy safety check trên bản sao trạng thái; không sửa session gốc. `WaitForResources` là request hợp lệ nhưng phải chờ, còn request vượt Need trả lỗi đầu vào có kiểu.

## 5. Ma trận yêu cầu – mô-đun – class/UI

| Mã | Yêu cầu người học | Mô-đun / hợp đồng | Class chính | Màn hình / hiển thị |
|---|---|---|---|---|
| R-CPU-01 | Chọn một trong sáu biến thể lập lịch | `CpuSchedulingInput` → `CpuSimulationSnapshot` | `CpuSchedulingModule`, các `ICpuSchedulingPolicy` | `CpuSchedulerView`, `CpuSchedulerViewModel` |
| R-CPU-02 | Xem từng bước, CPU rỗi, Gantt và metric | `ISimulationSession<CpuSimulationSnapshot>` | `TimelineSimulationSession<T>` | Playback chung, KPI, Gantt, ready queue, bảng metric, nhật ký |
| R-SYNC-01 | Minh họa producer/consumer, buffer đầy/rỗng và FIFO wake-up | `SynchronizationInput` scenario Producer–Consumer | `SynchronizationModule` | Actor, buffer, semaphore/mutex và wait queue |
| R-SYNC-02 | So sánh deadlock và tránh deadlock | Dining Philosophers naive/resource-ordering | `SynchronizationModule` | Actor, forks, wait-for edges, deadlock cycles |
| R-SYNC-03 | Chọn actor bằng tay hoặc chạy tự động xác định | `SimulationCommand` | `ReplayableSimulationSession<T>` | Chọn actor Ready; timer dùng dispatch tự động |
| R-RES-01 | Xem Allocation, Max, Need, Available/Work, safe sequence và quan hệ tài nguyên | `BankerInput` → `BankerSnapshot` | `BankerModule` | Ma trận Banker và RAG có hướng: Allocation, Need claim, Work/tổng tài nguyên |
| R-RES-02 | Thử request mà không sửa trạng thái mô phỏng | `BankerResourceRequest` → `BankerRequestResult` | `IBankerModule.TestRequest` | Granted/Wait/WouldBeUnsafe và safe sequence giả định |
| R-MEM-01 | Dịch địa chỉ và phân biệt TLB hit/page-table hit/page fault | `VirtualMemoryInput` → snapshot | `VirtualMemoryModule` | Chuỗi tham chiếu, dịch địa chỉ, KPI, frame, page table |
| R-MEM-02 | So sánh FIFO/LRU/Optimal/Clock, TLB và swap | `IPageReplacementPolicy` | Bốn policy; `VirtualMemoryModule` | Frame RAM, swap disk, mục TLB và nhật ký I/O |
| R-PLAY-01 | Đầu/đặt lại, phát/tạm dừng, tiến/lùi, tua timeline trace cố định và chỉnh nhịp diễn giải | `ISimulationSession<T>` | `PlaybackController<T>` | `PlaybackControls` dùng chung ở cả bốn mô-đun; trace tương tác hiển thị số bước đã chạy nhưng không cho tua tới bước chưa được tạo |
| R-ERR-01 | Báo lỗi đầu vào/lệnh mà không đổi trạng thái | `ValidationResult`, `SimulationError`, `SimulationStepResult<T>` | Module/session tương ứng | Status text của từng ViewModel |

## 6. Phân cấp màn hình và trách nhiệm UI

| Màn hình | Phân cấp thành phần | Đầu vào | Khu vực kết quả |
|---|---|---|---|
| Cửa sổ chính | `MainWindow` → sidebar điều hướng → `ContentControl` → `DataTemplate` cho mô-đun đang chọn | `MainWindowViewModel.NavigateTo` đổi vùng đang xem | Sidebar có trạng thái chọn; app bar chung; giữ nguyên các ViewModel phiên khi chuyển vùng; không chạy nghiệp vụ |
| Lập lịch CPU | Kịch bản mẫu tải sẵn → playback → diễn giải hành động → CPU core/ready queue/process cards/Gantt; cấu hình, metric và event log thu gọn | Dòng `mã, đến, burst, ưu tiên` và overhead số thập phân trong mục cấu hình | Ready queue giữ đúng thứ tự snapshot; lõi CPU, đoạn CS, tiến trình và Gantt phản ánh thời gian ảo; thẻ diễn giải nêu event và lý do chọn/chuyển trạng thái |
| Đồng bộ tiến trình | Kịch bản mẫu tải sẵn → playback/chọn actor → diễn giải hành động → actor/resource/buffer/wait graph; tham số, metric và event log thu gọn | Producer/consumer hoặc philosopher trong mục cấu hình | Actor vừa dispatch pulse; buffer và hàng đợi phản ánh snapshot; thẻ diễn giải giải thích produce/consume/block/wake; RAG lấy từ `WaitForEdge` |
| Cấp phát tài nguyên | Kịch bản mẫu tải sẵn → playback → diễn giải hành động → matrix/RAG; cấu hình, metric, request test và step log thu gọn | Resource names, Available, process IDs, Allocation/Max, request vector trong mục cấu hình/chi tiết | RAG có nút tiến trình/tài nguyên; cạnh liền `R → P` là Allocation, cạnh đứt `P → R` là Need claim chứ không phải request đang chờ; Work/tổng hiển thị trên nút tài nguyên; process vừa hoàn tất được pulse |
| Bộ nhớ ảo | Kịch bản mẫu tải sẵn → playback → diễn giải hành động → chuỗi tham chiếu/luồng dịch/frame/swap; cấu hình, metric và event log thu gọn | Địa chỉ thập phân hoặc `0x` hex, cấu hình địa chỉ trong mục thiết lập | Thẻ diễn giải phân biệt TLB hit, page-table hit, page fault, replacement và swap; frame và swap disk phản ánh snapshot |

Code-behind hiện chỉ được dùng cho hook vòng đời của view khi binding không đáp ứng được. Parse input đơn giản, gọi use case, quản lý timer và chuyển snapshot sang chuỗi hiển thị thuộc ViewModel/controller; lỗi parse đầu vào được biểu diễn bằng `SimulationError` mã `InvalidInput`, còn thuật toán và luật validation vẫn ở Domain.

## 7. API registry và trạng thái

Các API bên dưới là hợp đồng nội bộ ổn định của v1. API công khai mới phải được đăng ký tại đây. Hiện chưa có API persistence/export; chúng vẫn ngoài phạm vi.

| API | Owner | Chữ ký / trách nhiệm chính | Trạng thái |
|---|---|---|---|
| `ISimulationModule<TInput, TSnapshot>` | Domain/Simulation | `Validate(TInput)`; `CreateSession(TInput)` | Implemented |
| `ISimulationSession<TSnapshot>` | Domain/Simulation | Snapshot hiện tại; cờ tiến/lùi; `StepForward(command?)`, `StepBackward()`, `Reset()` | Implemented |
| `SimulationCreationResult<T>` | Domain/Simulation | Session thành công hoặc danh sách lỗi có kiểu | Implemented |
| `SimulationStepResult<T>` | Domain/Simulation | Thành công, hoàn tất, snapshot, event và lỗi tùy chọn | Implemented |
| `SimulationError`, `SimulationErrorCode`, `ValidationResult` | Domain/Simulation | Mã lỗi: InvalidInput, InvalidCommand, BoundaryReached, Deadlocked, InvariantViolation | Implemented |
| `SimulationEvent`, `SimulationCommand`, `SimulationFrame<T>` | Domain/Simulation | Event có thứ tự/thời điểm ảo dạng decimal; command dispatch; frame của timeline | Implemented |
| `TimelineSimulationSession<T>` | Domain/Simulation | Phát lại trace dựng sẵn, lùi và reset | Implemented |
| `ReplayableSimulationSession<T>` | Domain/Simulation | Chạy chuyển trạng thái theo command; lùi; bỏ nhánh tương lai khi tiến lại bằng lệnh mới | Implemented |
| `CpuSchedulingInput`, `CpuSimulationSnapshot`, `CpuProcessState`, `GanttSegment`, `GanttSegmentKind`, `CpuMetrics` | Domain/Cpu | Thời gian CPU decimal; process state có ContextSwitching; Gantt phân biệt execution/idle/context switch; metrics số lượt và tổng overhead | Implemented |
| `CpuSchedulingModule`, `ICpuSchedulingPolicy` | Domain/Cpu | Validate/tạo timeline; chọn tiến trình, quyết định ngắt và áp overhead khi đổi tiến trình | Implemented |
| `SynchronizationModule` | Domain/Synchronization | Validate/tạo replayable session cho hai scenario | Implemented |
| `BankerInput`, `BankerProcessInput`, `BankerResourceRequest`, `BankerProcessSnapshot`, `BankerSnapshot`, `BankerRunState`, `BankerRequestDisposition`, `BankerRequestResult` | Domain/ResourceAllocation | Ma trận cấp phát, trạng thái Work/Finish, safe sequence và kết quả request test có kiểu | Implemented |
| `IBankerModule`, `BankerModule` | Domain/ResourceAllocation | Safety algorithm theo từng bước; kiểm tra request trên bản sao trạng thái | Implemented |
| `VirtualMemoryModule`, `IPageReplacementPolicy` | Domain/Memory | Validate/tạo trace truy cập; chọn victim trang; theo dõi swap-in/out | Implemented |
| `MemoryMetrics`, `VirtualMemorySnapshot` | Domain/Memory | Đếm swap-in/out và công khai danh sách trang hiện đang ở swap disk | Implemented |
| `SimulationCatalog` | Application | Tạo session theo module và gọi Banker request test; constructor cho composition/injection | Implemented |
| `PlaybackController<T>` | Wpf/Playback | Điều khiển timer, đồng bộ timeline của trace cố định, hiển thị vị trí luồng tương tác và phát snapshot cuối sau thao tác tua | Implemented |
| `MainWindowViewModel`, `SimulationArea`, `SimulationNavigationItem`, `ISimulationAreaViewModel` | Wpf/ViewModels | Composition bốn ViewModel mô-đun; chọn vùng đang xem; interface xác định vùng cho DataTemplate | Implemented |
| `MetricCardVisual`, `MetricTone` | Wpf/ViewModels | Projection chung cho một KPI có nhãn, giá trị, ghi chú và sắc thái hiển thị | Implemented |
| `SimulationActionVisual`, `SimulationActionNarrator` | Wpf/ViewModels | Chuyển event Domain mới nhất thành tiêu đề, mô tả nguyên nhân, sắc thái trình bày và phiên bản pulse; chỉ dùng cho binding, không thay đổi session | Implemented |
| `MemoryReferenceVisual`, `MemoryFrameVisual` | Wpf/ViewModels | Projection UI của chuỗi địa chỉ/trang theo access và các frame RAM hiện tại | Implemented |
| `WaitGraphEdgeVisual` | Wpf/ViewModels | Projection cạnh yêu cầu actor→resource và cạnh đang giữ resource→actor; không sở hữu luật deadlock | Implemented |
| `BankerRagProjector`, `BankerRagGraphVisual`, `BankerRagNodeVisual`, `BankerGraphEdgeVisual` | Wpf/ViewModels | Chiếu snapshot Banker thành nút tiến trình/tài nguyên, cạnh có hướng và vị trí để render RAG | Implemented |
| `BankerProcessVisual` | Wpf/ViewModels | Projection một hàng matrix Banker và pulse khi process hoàn tất | Implemented |
| `PulseAnimation.PulseVersion` | Wpf/Animations | Attached property phát xung scale ngắn khi phiên bản snapshot hoặc hành động diễn giải thay đổi; tắt theo `SystemParameters.ClientAreaAnimation` | Implemented |
| `ProgressValueAnimation.TargetValue` | Wpf/Animations | Attached property làm mượt `ProgressBar.Value` tới giá trị chiếu từ snapshot; tắt theo `SystemParameters.ClientAreaAnimation` | Implemented |
| `CpuProcessVisual` | Wpf/ViewModels | Observable display model giữ thẻ tiến trình ổn định giữa các snapshot để thanh tiến độ nội suy được giá trị | Implemented |
| `GanttSegmentVisual`, `SynchronizationActorDisplay`, `SynchronizationResourceDisplay`, `BufferSlotVisual`, `MemoryFlowStageVisual` | Wpf/ViewModels | Projection chỉ dành cho UI: đoạn Gantt, actor/tài nguyên, ô buffer hữu hạn và bước dịch địa chỉ có trạng thái hiển thị | Implemented |
| Lưu/mở kịch bản, persistence hoặc export | Ngoài phạm vi v1 | Chưa có chữ ký/hợp đồng để tránh vô tình xem là tính năng đã cam kết | Planned |

Không có API `Deprecated` trong v1. Nếu API bị thay, giữ dòng registry ở trạng thái Deprecated cho đến khi caller được di chuyển và mã cũ được gỡ.

## 8. Bảng class, quyền sở hữu và hàm chính

| Namespace / class | Sở hữu | Hàm / cộng tác viên chính | Trạng thái |
|---|---|---|---|
| `Domain.Simulation.ISimulationModule<TInput,T>` | Vòng đời module | `Validate`, `CreateSession`; trả `ValidationResult`/`SimulationCreationResult<T>` | Implemented |
| `Domain.Simulation.ISimulationSession<T>` | Điều khiển snapshot và vị trí phát lại | `CurrentSnapshot`, `StepPosition`, `StepCount?`, cờ CanStep, `StepForward`, `StepBackward`, `Reset` | Implemented |
| `TimelineSimulationSession<T>` | Cursor trên trace dựng sẵn | Công khai vị trí hiện tại và tổng số frame để player hiển thị/tua timeline; không chứa luật module | Implemented |
| `ReplayableSimulationSession<T>` | History và cursor của chuyển trạng thái tương tác | Công khai vị trí đã chạy; `StepCount` là null vì trace tương lai chưa được tạo trước | Implemented |
| `CpuSchedulingModule` | Validate CPU, mô phỏng từng đơn vị CPU và overhead đổi tiến trình, tính Gantt/metrics | Dùng policy qua `ICpuSchedulingPolicy`; context switch được ghi thành segment/event riêng | Implemented |
| `FcfsSchedulingPolicy`, `SjfSchedulingPolicy`, `SrtfSchedulingPolicy`, `PrioritySchedulingPolicy`, `RoundRobinSchedulingPolicy` | Thứ tự chọn tiến trình và điều kiện preempt | `SelectNext`, `ShouldPreempt`; do module gọi | Implemented |
| `SynchronizationModule` | Mô hình Producer–Consumer, Dining Philosophers, semaphore/mutex, FIFO wait queue, cycle detection | Tạo `ReplayableSimulationSession<SynchronizationSnapshot>`; nhận `SimulationCommand`; ghi `LastDispatchedActorId` trong snapshot để UI nêu rõ actor vừa chạy | Implemented |
| `BankerModule` | Ma trận Allocation/Max/Need, Work/Finish, safe sequence và request grant/wait/deny | Tạo timeline safety; request chạy trên trạng thái giả định riêng và không sửa session đang mở | Implemented |
| `VirtualMemoryModule` | Tách địa chỉ, page table/frame/TLB/swap, metrics và trace truy cập | Dùng `IPageReplacementPolicy`; tạo timeline và theo dõi các trang page-out/page-in | Implemented |
| `FifoPageReplacementPolicy`, `LruPageReplacementPolicy`, `OptimalPageReplacementPolicy`, `ClockPageReplacementPolicy` | Chọn frame nạn nhân | `SelectVictim`; không quản lý page table/TLB | Implemented |
| `SimulationCatalog` | Điểm vào Application cho UI | Tạo bốn loại session; `TestBankerRequest` | Implemented |
| `PlaybackController<T>` | Timer, play/pause, forward/back/reset, tua trace cố định, nhịp phát và nhãn timeline | Gọi `ISimulationSession<T>`; tua theo cursor rồi chỉ phát snapshot cuối cho ViewModel; UI dùng một implementation chung | Implemented |
| `MainWindowViewModel` | Điều hướng và composition bốn ViewModel mô-đun | Đổi `SelectedArea`, cung cấp `ISimulationAreaViewModel` cho DataTemplate; không tạo hoặc sửa session mô phỏng | Implemented |
| `ISimulationAreaViewModel` | Hợp đồng viewmodel của một mô-đun ở lớp WPF | Cung cấp `Area`; các module ViewModel thực thi để MainWindow chọn đúng DataTemplate | Implemented |
| `SimulationNavigationItem` | Label và selected state của một mục sidebar | Được `MainWindowViewModel` sở hữu; không chứa logic mô phỏng | Implemented |
| `CpuSchedulerViewModel`, `SynchronizationViewModel`, `VirtualMemoryViewModel`, `BankerViewModel` | Tạo kịch bản mẫu mặc định, parse input, gọi catalog, chiếu snapshot/event/metrics thành binding và display record | Không chứa thuật toán mô phỏng; lỗi parse trả `SimulationError` | Implemented |
| `MetricCardVisual`, `SimulationActionVisual`, `MemoryReferenceVisual`, `MemoryFrameVisual`, `WaitGraphEdgeVisual`, `BankerProcessVisual`, `BankerRagGraphVisual`, `BankerRagNodeVisual`, `BankerGraphEdgeVisual` | Projection UI; gắn nhãn/trạng thái, diễn giải hành động và pulse khi bước thay đổi | Chỉ được ViewModel dùng làm đầu vào binding; Domain không tham chiếu | Implemented |
| `CpuProcessVisual`, `GanttSegmentVisual`, `SynchronizationActorDisplay`, `BufferSlotVisual`, `MemoryFlowStageVisual` | Mô hình hiển thị của WPF, gồm label/trạng thái và `PulseVersion` nếu cần; `CpuProcessVisual` giữ binding ổn định để nội suy progress | Chiếu từ snapshot; không được Domain dùng làm đầu vào | Implemented |
| `PulseAnimation` | Hiệu ứng trình bày dùng attached property | Scale pulse tối đa 6% trong 150 ms mỗi chiều, không lặp; áp dụng cho visual bị tác động và thẻ diễn giải hiện tại; tôn trọng thiết lập Windows tắt animation | Implemented |
| `ProgressValueAnimation` | Hiệu ứng trình bày cho tiến độ CPU | Nội suy ProgressBar trong 220 ms; không đổi thời gian hoặc kết quả mô phỏng; tôn trọng thiết lập Windows tắt animation | Implemented |
| `CpuSchedulerView`, `SynchronizationView`, `VirtualMemoryView`, `BankerView`, `PlaybackControls` | Hiển thị và binding | Không sở hữu trạng thái domain | Implemented |

DTO đầu vào/đầu ra là immutable record: `CpuSchedulingInput`/`CpuSimulationSnapshot`, `SynchronizationInput`/`SynchronizationSnapshot`, `VirtualMemoryInput`/`VirtualMemorySnapshot`, `BankerInput`/`BankerSnapshot`. Snapshot và các collection tạo từ Domain cần giữ bất biến với caller; không thay đổi collection history của session từ bên ngoài.

## 9. Luồng UI – Application – Domain

```text
Sidebar button → MainWindowViewModel.SelectedArea → ContentControl DataTemplate → ViewModel mô-đun
Form → Module ViewModel → SimulationCatalog → ISimulationModule → ISimulationSession
Banker request → BankerViewModel → SimulationCatalog → IBankerModule.TestRequest → immutable tentative state → safety result
Khi ViewModel được tạo, nó nạp kịch bản mẫu xác định trước; người học bấm playback để tiến/lùi hoặc phát.
Playback button/timer/timeline → PlaybackController → ISimulationSession.StepForward/StepBackward/Reset
ISimulationSession result + StepPosition/StepCount → PlaybackController timeline label → ViewModel projection → visual + SimulationActionNarrator → WPF binding
```

Timer thuộc WPF Dispatcher. Bước tay, tua và timer gọi cùng hợp đồng session; chỉ command factory cho biết dispatch actor tay hay tự động. Tua chỉ áp dụng cho trace có tổng số bước cố định; luồng tương tác báo vị trí đang chạy và không hứa trước thời lượng. Nhịp mặc định 1.8 giây mỗi bước, cho phép điều chỉnh 0.5–5 giây để đọc phần diễn giải. Domain không ngủ, không biết dispatcher, và không tự chạy nền.

## 10. Kiểm tra đầu vào và biên phiên

| Mô-đun | Luật đầu vào và giới hạn hiện triển khai |
|---|---|
| CPU | 1–50 tiến trình; mã không rỗng/duy nhất không phân biệt hoa thường; đến 0–1,000,000; burst > 0; tổng burst ≤ 10,000; ưu tiên ≥ 0; quantum > 0 cho RR; overhead từ 0 đến 1.000 đơn vị. |
| Producer–Consumer | Capacity ≥ 1; producer và consumer mỗi loại 1–20; sản phẩm mỗi actor 1–100; tổng số sản xuất phải bằng tổng số tiêu thụ. |
| Dining Philosophers | 3–20 triết gia; 1–100 bữa mỗi người; chỉ chấp nhận hai mode đã định nghĩa. |
| Banker | 1–8 tài nguyên, 1–20 tiến trình; vector đủ chiều, ID không trùng; đơn vị không âm; Allocation ≤ Max; request không vượt Need; request vượt Available trả `WaitForResources`. |
| Bộ nhớ ảo | Có 1–5,000 tham chiếu; địa chỉ 1–64 bit; page size lũy thừa 2 và không vượt không gian địa chỉ; 1–1,024 frame; TLB 0–4,096; địa chỉ trong miền; policy đã hỗ trợ. |
| Lệnh/session | Actor chỉ được chọn khi Ready; lệnh tay cần actor ID; step ở biên trả typed error; lỗi lệnh không làm đổi snapshot hoặc xóa nhánh tương lai. |

Khi lùi rồi tiến bằng command khác, session tương tác bỏ nhánh lịch sử phía trước chỉ sau khi transition mới thành công. Reset đưa cursor về trạng thái đầu. Cùng input và cùng command sequence phải cho cùng snapshot/event.

## 11. UML và các bảng phân cấp

`docs/uml/` chứa PlantUML làm hình chiếu của các hợp đồng phía trên:

| Sơ đồ | Mục đích |
|---|---|
| `use-cases.puml` | Actor người học và thao tác cấu hình, chạy, xem kết quả |
| `components.puml` | Ranh giới project và hướng phụ thuộc |
| `classes.puml` | Contract, module, session, policy, Catalog và các view projection |
| `simulation-sequence.puml` | Tạo session và một bước playback qua UI/domain |
| `process-state.puml` | Trạng thái tiến trình CPU, actor đồng bộ và trạng thái deadlock của scenario |
| `ui-hierarchy.puml` | Phân cấp MainWindow → sidebar/app bar → ContentControl/DataTemplate → view/ViewModel/control dùng chung |
| `ui-animation-sequence.puml` | Snapshot → view projection → pulse minh họa; animation không điều khiển Domain |

Nếu UML và baseline không khớp, sửa cả hai cùng mã. Không để diagram mô tả trạng thái hoặc method không tồn tại trong contract.

## 12. Ma trận nghiệm thu

| Khu vực | Ca kiểm tra bắt buộc |
|---|---|
| CPU | Timeline mẫu cho FCFS/SJF/SRTF/Priority/RR; ngắt; tie; idle; RR arrival tại biên; overhead 0 và phân số; CS sau idle/cùng PID; metric; input sai. |
| Đồng bộ | Buffer đầy/rỗng; đánh thức FIFO; chọn actor tay/tự động; deadlock và wait cycle; resource ordering; lệnh actor sai không đổi state. |
| Banker | Safe/unsafe state; Need/Work/Finish theo bước; request được cấp, phải chờ, gây unsafe và sai chiều; trạng thái gốc không đổi sau request test. |
| Bộ nhớ | Tách/dịch địa chỉ; 4 replacement policies; TLB hit/miss tách page fault; xóa mapping cũ; swap-out/in; TLB tắt; đầu vào sai. |
| Session | Forward/back/reset; chạy lại xác định; branch mới sau rewind; failed command bảo toàn history và snapshot. |
| Tích hợp | Mỗi mô-đun tạo đúng module; điều hướng không làm mất ViewModel phiên; play/pause/step/back/reset chỉ chiếu snapshot; không có luật nghiệp vụ trong code-behind. |

## 13. Thứ tự phát triển đã áp dụng

1. Chốt tài liệu chuẩn, read-first rule, bảng phân cấp và UML.
2. Dựng solution, contract và project skeleton.
3. Dựng WPF shell và bốn màn hình MVVM.
4. Tích hợp CPU, Banker, bộ nhớ và đồng bộ vào cùng ranh giới Application/Domain.
5. Đồng bộ registry/UML với mã và kiểm tra build, test, luồng UI.

Persistence, import/export, plug-in và chạy tiến trình hệ điều hành thật nằm ngoài phạm vi phiên bản đầu.

## 14. Lộ trình phân tích thiết kế hướng đối tượng

| Khái niệm | Nơi quan sát | Bằng chứng trong thiết kế |
|---|---|---|
| Đóng gói | `Domain/Cpu`, `Domain/Synchronization`, `Domain/ResourceAllocation`, `Domain/Memory`, `Domain/Simulation` | Module sở hữu luật chuyển trạng thái; session sở hữu cursor/history; snapshot công khai dưới dạng record và collection chỉ đọc. |
| Trừu tượng | `ISimulationModule<TInput,TSnapshot>`, `ISimulationSession<TSnapshot>`, `ICpuSchedulingPolicy`, `IPageReplacementPolicy`, `IBankerModule`, `ISimulationAreaViewModel` | Caller dựa vào hợp đồng module/session/policy; shell dùng hợp đồng vùng mô phỏng để chọn DataTemplate; request test tách khỏi session playback. |
| Đa hình | `CpuSchedulingPolicies.cs`, `PageReplacementPolicies.cs` | Module chọn chiến lược qua interface; FCFS/SJF/SRTF/Priority/Round Robin và FIFO/LRU/Optimal/Clock thay thế lẫn nhau trong vai trò policy tương ứng. |
| Kết hợp đối tượng | `Wpf/ViewModels/MainWindowViewModel.cs`, `Application/SimulationCatalog.cs`, `Wpf/App.xaml.cs`, `Wpf/Playback/PlaybackController.cs` | Shell composition bốn ViewModel mô-đun; Catalog nhận module qua constructor; composition root dựng ứng dụng; playback điều khiển mọi phiên qua `ISimulationSession<TSnapshot>`. |
| Trách nhiệm và phụ thuộc | Các project `OsSim.Domain`, `OsSim.Application`, `OsSim.Wpf` | Domain xử lý mô phỏng, Application tạo phiên, WPF nhận input/chiếu snapshot/điều khiển timer; phụ thuộc đi theo `Wpf → Application → Domain`. |
| Kế thừa | `Wpf/ViewModels` | ViewModel kế thừa `ObservableObject` để hỗ trợ binding; Domain không thêm cây lớp cơ sở để chia sẻ thuật toán, mà dùng interface và composition. |

Sơ đồ `docs/uml/classes.puml` cho quan hệ contract–module–policy–session; `docs/uml/components.puml` cho hướng phụ thuộc giữa project. Bảng class ownership tại mục 8 vẫn là chuẩn khi cần xác định nơi sửa một trách nhiệm cụ thể.

