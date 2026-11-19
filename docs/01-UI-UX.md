# UI/UX và minh họa chuyển động

Tài liệu này mô tả cách áp dụng baseline vào giao diện WPF. `docs/00-BASELINE.md` vẫn là nguồn chuẩn cho hợp đồng mô phỏng và class ownership; thay đổi ở đây không tạo thêm luật Domain.

## Phong cách hiển thị

| Khu vực | Quy ước |
|---|---|
| Khung ứng dụng | Sidebar điều hướng bốn mô-đun, app bar thấp, workspace cuộn theo chiều dọc. `MainWindowViewModel` chọn DataTemplate đang hiển thị. |
| Nền và bề mặt | Nền tím xám nhạt; primary indigo; xanh lá cho hit/hoạt động, đỏ cho fault/deadlock, cam cho chờ; bề mặt phẳng, Border/GroupBox bo nhẹ. |
| Nút chính | Một nút tạo mô phỏng trong nhóm cấu hình. Điều khiển còn lại dùng kiểu secondary. |
| Chữ | Segoe UI; tiêu đề module, mô tả ngắn, label; thuật ngữ CPU/TLB/FIFO giữ nguyên để thống nhất môn học. |
| Trạng thái | Luôn có nhãn văn bản; màu và animation chỉ hỗ trợ nhận biết, không là kênh thông tin duy nhất. |
| Mật độ | Tải sẵn một kịch bản nhỏ; thu gọn phần cấu hình, metric và nhật ký; dành vùng đầu màn hình cho playback và lời giải thích bước hiện tại, sau đó là visual mô phỏng. |
| Kích thước | Màn hình cuộn khi thiếu chiều cao; input dùng WrapPanel/grid; chuỗi tham chiếu cuộn ngang; bảng và nhật ký có chiều cao gọn. |

## Phân cấp màn hình và lớp trình bày

```text
MainWindow
├── Sidebar ── MainWindowViewModel ── SimulationNavigationItem[]
├── App bar (tên phòng lab + thời gian ảo)
└── ContentControl ── DataTemplate theo ActiveViewModel
   ├── CpuSchedulerView ── CpuSchedulerViewModel
   │  ├── Expander: ProcessInput / Algorithm / Quantum / ContextSwitchOverhead
   │  ├── Video-style PlaybackControls ── PlaybackController<CpuSimulationSnapshot>
   │  ├── SimulationActionVisual + Expander: metrics
   │  ├── CPU Core + Ready Queue + Process cards + Gantt
   │  └── Expander: per-process details + event log
   ├── SynchronizationView ── SynchronizationViewModel
   │  ├── Expander: scenario parameters
   │  ├── Video-style PlaybackControls ── PlaybackController<SynchronizationSnapshot>
   │  ├── Actor selection + SimulationActionVisual + Expander: metrics
   │  ├── Actor cards + resource table + finite buffer slots
   │  ├── Wait graph edges + deadlock cycles
   │  └── Expander: event log
   ├── BankerView ── BankerViewModel
   │  ├── Expander: ResourceNames / Available / Allocation / MaxDemand
   │  ├── Video-style PlaybackControls ── PlaybackController<BankerSnapshot>
   │  ├── SimulationActionVisual + Expander: metrics
   │  ├── Banker matrix + directed RAG with Work/total resource nodes
   │  └── Expander: request test trên bản sao trạng thái + step trace
   └── VirtualMemoryView ── VirtualMemoryViewModel
      ├── Expander: address input + memory configuration
      ├── Video-style PlaybackControls ── PlaybackController<VirtualMemorySnapshot>
      ├── SimulationActionVisual + Expander: metrics
      ├── Reference stream + translation flow
      └── Frame RAM cards + swap disk + page table + TLB + Expander: event log
```

Khi ứng dụng mở, mỗi ViewModel tạo một session mẫu xác định từ input mặc định. Mô phỏng ở trạng thái ban đầu và chờ người học bấm Phát hoặc Tiến bước; không tự chạy animation. Mục thiết lập được mở khi cần đổi kịch bản rồi tạo lại session.

`PlaybackControls` dùng chung cách bố trí kiểu trình phát video: Đầu, Lùi, Phát/Tạm dừng, Tiến; một timeline có nhãn vị trí bước; và thanh chỉnh nhịp phát. Trace dựng sẵn cho phép kéo timeline để tua tới frame; `PlaybackController` đi qua session nhưng chỉ công bố snapshot ở đích để tránh phát dồn animation trung gian. Session tương tác chỉ công bố số bước đã đi và không cho tua tới tương lai chưa được tạo. Nhịp mặc định là 1.8 giây mỗi bước, có thể chỉnh từ 0.5 đến 5 giây; lời giải thích của bước hiện tại nằm cạnh vùng mô phỏng, pulse khi bước đổi và giữ nguyên khi tạm dừng; dòng trạng thái player chỉ báo trạng thái playback để không lặp lại mô tả của thẻ hành động.

## Minh họa theo snapshot

| Mô-đun | Dữ liệu quyết định visual | Cách minh họa |
|---|---|---|
| CPU | `RunningProcessId`, `DispatchingProcessId`, `ReadyQueue`, `Time`, process state và metrics | Gantt có đoạn CS theo overhead; utilization tính CPU execution và overhead; bộ đếm CS lấy từ snapshot. |
| Đồng bộ | `LastDispatchedActorId`, `StepNumber`, `WaitForGraph`, resource/buffer snapshot | Actor vừa dispatch pulse; thẻ có trạng thái bằng chữ; RAG chiếu actor yêu cầu resource và resource đang do actor giữ; deadlock cycles lấy từ snapshot. |
| Banker | `Available`, `Work`, `Processes[].Allocation/MaxDemand/Need`, `SafeSequence`, request result | Nút tròn là process; nút chữ nhật hiển thị Work/tổng tài nguyên. Cạnh liền `R → P` biểu diễn Allocation; cạnh đứt `P → R` biểu diễn Need claim, không phải request đang chờ. Process hoàn tất được pulse và các cạnh của nó được gỡ khỏi graph. Tất cả chỉ chiếu snapshot Domain; request test không thay đổi phiên đang phát. |
| Bộ nhớ ảo | `AccessIndex`, `Outcome`, `Metrics`, VPN/offset/frame/address, `SwapPages` | Dòng tham chiếu ghi hit/fault; frame và swap disk phản ánh page replacement/swap I/O; pipeline có trạng thái và bước bỏ qua. |

`SimulationActionNarrator` ở WPF ánh xạ event mới nhất thành `SimulationActionVisual` gồm bước, tên hành động, mô tả từ Domain và phần giải thích ngắn. Đây là projection cho học tập, không phải luật mô phỏng; ViewModel thông báo binding khi snapshot đổi để lời giải thích đi cùng actor/frame/segment vừa được pulse.

`BankerRagProjector` tách việc dựng bố cục RAG khỏi `BankerViewModel`. Projector tạo hai loại edge từ snapshot hiện tại, tính Work/tổng cho từng loại tài nguyên và giữ nút process hoàn tất để người học theo dõi safe sequence; edge của process đã hoàn tất không còn xuất hiện vì Allocation đã được trả về Work trong safety trace.

Chỉ trình bày số liệu có trong hợp đồng hiện tại. Banker dùng safety algorithm độc lập, còn deadlock wait-for graph của mô-đun đồng bộ vẫn lấy từ snapshot actor/resource. Swap chỉ theo dõi trang bị đưa ra hoặc nạp lại; mô hình chưa mô phỏng dữ liệu byte, thao tác ghi hay dirty bit.

### Quy tắc animation

- `PulseAnimation.PulseVersion` là attached property ở lớp WPF. Lời dẫn lấy phiên bản từ event hiện tại; ViewModel chiếu thay đổi snapshot sang các visual bị tác động. Domain không gọi animation, dispatcher hoặc timer trình bày.
- Mỗi pulse phóng to tối đa 6% rồi trở lại trong 150 ms mỗi chiều; không lặp liên tục và không thay đổi thời gian mô phỏng.
- Thanh tiến độ CPU nội suy trong 220 ms tới tỷ lệ mới; hiệu ứng chỉ làm mượt giá trị hiển thị, không thay đổi tốc độ hay kết quả mô phỏng.
- Tự động phát chỉ tạo pulse khi snapshot đổi. Pause không tạo chuyển động mới; step backward làm visual phản ánh snapshot cũ.
- Tôn trọng `SystemParameters.ClientAreaAnimation`; nếu Windows tắt animation vùng nội dung thì visual vẫn giữ nguyên label/trạng thái nhưng không scale.
- Màu, kích thước và pulse không được làm mất chỉ báo bằng chữ. Trạng thái trong bảng/nhật ký vẫn là nguồn giải thích.
- Thẻ diễn giải luôn hiển thị kể cả khi Windows tắt animation; nó mô tả snapshot hiện tại và không tự tiến bước.
- Playback timeline phản ánh cursor của session: trace dựng sẵn cho phép tua theo bước; trace tương tác không giả định trước số bước và giữ slider ở trạng thái không tua được.

## Sơ đồ liên quan

- `docs/uml/ui-hierarchy.puml`: phân cấp cửa sổ và trách nhiệm các view.
- `docs/uml/ui-animation-sequence.puml`: luồng snapshot tới pulse presentation.
- `docs/uml/components.puml`: ranh giới WPF/Application/Domain.
- `docs/uml/classes.puml`: contract, module và model hiển thị.

