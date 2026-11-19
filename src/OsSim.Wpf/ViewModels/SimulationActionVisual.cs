using OsSim.Domain.Simulation;

namespace OsSim.Wpf.ViewModels;

/// <summary>
/// Presentation-only explanation for the latest action in a simulation snapshot.
/// </summary>
public sealed record SimulationActionVisual(
    string StepLabel,
    string Title,
    string Description,
    string Explanation,
    MetricTone Tone,
    long PulseVersion = 0);

/// <summary>
/// Translates domain events into short learning cues without changing simulation state.
/// </summary>
public static class SimulationActionNarrator
{
    public static SimulationActionVisual Describe(
        SimulationArea area,
        SimulationEvent? simulationEvent,
        bool isDeadlocked = false)
    {
        if (area == SimulationArea.Synchronization && isDeadlocked)
        {
            return new SimulationActionVisual(
                "DEADLOCK",
                "Không còn actor nào có thể tiếp tục",
                simulationEvent?.Description ?? "Các actor còn việc nhưng không actor nào ở trạng thái sẵn sàng.",
                "Các actor đang chờ tài nguyên do những actor khác giữ; đồ thị chờ cho thấy chu trình gây bế tắc.",
                MetricTone.Danger,
                simulationEvent?.Sequence ?? 1);
        }

        if (simulationEvent is null)
        {
            var (title, description, explanation) = area switch
            {
                SimulationArea.Cpu => (
                    "Kịch bản CPU đã sẵn sàng",
                    "Chưa có bước nào được phát lại.",
                    "Nhấn Tiến bước hoặc Phát để xem tiến trình được chọn và cập nhật trên Gantt."),
                SimulationArea.Synchronization => (
                    "Kịch bản đồng bộ đã sẵn sàng",
                    "Các actor đang ở trạng thái ban đầu.",
                    "Mỗi bước mô phỏng một actor; buffer và hàng đợi thay đổi theo hành động đó."),
                SimulationArea.ResourceAllocation => (
                    "Kịch bản Banker đã sẵn sàng",
                    "Safety check chưa thực hiện bước đầu tiên.",
                    "Theo dõi Need ≤ Work để hiểu vì sao một tiến trình có thể hoàn tất an toàn."),
                _ => (
                    "Kịch bản bộ nhớ đã sẵn sàng",
                    "Chưa có địa chỉ nào được truy cập.",
                    "Tiến từng bước để thấy đường dịch địa chỉ, frame RAM và swap cập nhật."),
            };

            return new SimulationActionVisual("SẴN SÀNG", title, description, explanation, MetricTone.Primary);
        }

        var (actionTitle, explanationText, tone) = (area, simulationEvent.Code) switch
        {
            (SimulationArea.Cpu, "ARRIVAL") => (
                "Tiến trình vào hàng đợi Ready",
                "Đến thời điểm này, tiến trình mới được phép cạnh tranh CPU theo thuật toán đã chọn.",
                MetricTone.Primary),
            (SimulationArea.Cpu, "DISPATCH") => (
                "Bộ lập lịch cấp CPU",
                "Tiến trình được lấy khỏi Ready Queue để chạy trên lõi xử lý.",
                MetricTone.Success),
            (SimulationArea.Cpu, "CPU_TICK") => (
                "CPU thực thi tiến trình",
                "Burst còn lại giảm và thời gian ảo tiến lên; Gantt ghi lại khoảng chạy này.",
                MetricTone.Primary),
            (SimulationArea.Cpu, "CONTEXT_SWITCH") => (
                "CPU đang đổi ngữ cảnh",
                "Khoảng overhead được tính trước khi tiến trình đã chọn bắt đầu chạy.",
                MetricTone.Warning),
            (SimulationArea.Cpu, "PREEMPT") => (
                "Tiến trình nhường CPU",
                "Một tiến trình có thứ tự ưu tiên tốt hơn đã đến; tiến trình đang chạy trở về Ready.",
                MetricTone.Warning),
            (SimulationArea.Cpu, "QUANTUM_EXPIRED") => (
                "Round Robin hết quantum",
                "Nếu chưa hoàn tất, tiến trình sẽ nhường CPU để các tiến trình khác có lượt chạy.",
                MetricTone.Warning),
            (SimulationArea.Cpu, "CPU_IDLE") => (
                "CPU đang rỗi",
                "Không có tiến trình Ready; thời gian ảo tiến tới mốc đến tiếp theo.",
                MetricTone.Warning),
            (SimulationArea.Cpu, "COMPLETED") => (
                "Tiến trình hoàn tất",
                "CPU burst đã dùng hết; tiến trình rời hệ thống và các metric được cập nhật.",
                MetricTone.Success),

            (SimulationArea.Synchronization, "PRODUCE") => (
                "Producer đưa sản phẩm vào buffer",
                "Một ô trống được dùng; sản phẩm mới có thể đánh thức consumer đang chờ.",
                MetricTone.Success),
            (SimulationArea.Synchronization, "CONSUME") => (
                "Consumer lấy sản phẩm khỏi buffer",
                "Buffer có thêm ô trống để producer tiếp tục; thứ tự lấy tuân theo mô hình hàng đợi.",
                MetricTone.Success),
            (SimulationArea.Synchronization, "BLOCK") => (
                "Actor bị chặn",
                "Tài nguyên cần dùng chưa sẵn sàng nên actor được đưa vào hàng đợi chờ.",
                MetricTone.Warning),
            (SimulationArea.Synchronization, "WAKE") => (
                "Đánh thức actor đang chờ",
                "Tài nguyên được nhả và actor đầu hàng đợi FIFO được phép tiếp tục.",
                MetricTone.Success),
            (SimulationArea.Synchronization, "ACQUIRE") => (
                "Actor lấy được tài nguyên",
                "Quyền sở hữu tài nguyên được ghi vào snapshot trước khi actor tiếp tục.",
                MetricTone.Primary),
            (SimulationArea.Synchronization, "RELEASE") => (
                "Actor nhả tài nguyên",
                "Tài nguyên trở nên khả dụng; hàng đợi chờ có thể được xét để đánh thức actor.",
                MetricTone.Success),
            (SimulationArea.Synchronization, "SIGNAL") => (
                "Actor phát tín hiệu",
                "Semaphore báo rằng một điều kiện tài nguyên đã thay đổi.",
                MetricTone.Primary),
            (SimulationArea.Synchronization, "THINK") => (
                "Triết gia đang suy nghĩ",
                "Actor chưa giữ đũa nên chưa tranh chấp tài nguyên với các actor khác.",
                MetricTone.Primary),
            (SimulationArea.Synchronization, "EAT") => (
                "Triết gia bắt đầu ăn",
                "Actor đã lấy đủ tài nguyên cần thiết để thực hiện một lượt ăn.",
                MetricTone.Success),
            (SimulationArea.Synchronization, "UNIT_COMPLETED") => (
                "Actor hoàn tất một lượt công việc",
                "Tiến độ actor tăng một đơn vị và trạng thái được chiếu lại lên màn hình.",
                MetricTone.Success),
            (SimulationArea.Synchronization, "TERMINATED") => (
                "Actor đã kết thúc",
                "Actor hoàn thành công việc được giao và không còn được dispatch.",
                MetricTone.Success),

            (SimulationArea.ResourceAllocation, "SAFE_STEP") => (
                "Tiến trình có thể hoàn tất an toàn",
                "Need không vượt Work hiện tại; sau khi hoàn tất, Allocation được trả về Work.",
                MetricTone.Success),
            (SimulationArea.ResourceAllocation, "UNSAFE_STATE") => (
                "Không tìm thấy bước an toàn",
                "Không tiến trình chưa hoàn tất nào có Need ≤ Work; trạng thái được kết luận unsafe.",
                MetricTone.Danger),

            (SimulationArea.Memory, "TLBHIT") => (
                "Địa chỉ được tìm thấy trong TLB",
                "Ánh xạ trang đã có trong cache dịch địa chỉ nên không cần tra bảng trang.",
                MetricTone.Success),
            (SimulationArea.Memory, "PAGETABLEHIT") => (
                "Bảng trang tìm thấy ánh xạ",
                "TLB không có mục phù hợp nhưng trang vẫn đang ở RAM; ánh xạ được nạp lại vào TLB.",
                MetricTone.Success),
            (SimulationArea.Memory, "PAGEFAULT") => (
                "Xảy ra lỗi trang",
                "Trang chưa có trong RAM nên hệ thống phải nạp trang vào một frame.",
                MetricTone.Warning),
            (SimulationArea.Memory, "PAGE_REPLACEMENT") => (
                "Một frame RAM được thay trang",
                "Khi RAM đã đầy, policy chọn trang nạn nhân để nhường frame cho trang mới.",
                MetricTone.Warning),
            (SimulationArea.Memory, "SWAP_OUT") => (
                "Trang được đưa ra swap",
                "Trang nạn nhân rời RAM và được ghi vào danh sách swap của mô hình.",
                MetricTone.Warning),
            (SimulationArea.Memory, "SWAP_IN") => (
                "Trang được nạp lại từ swap",
                "Trang được truy cập lại và chuyển từ swap disk mô phỏng vào frame RAM.",
                MetricTone.Success),

            _ => ("Trạng thái mô phỏng thay đổi", "Snapshot mới phản ánh hành động vừa xảy ra.", MetricTone.Primary),
        };

        return new SimulationActionVisual(
            $"BƯỚC {simulationEvent.Sequence}",
            actionTitle,
            simulationEvent.Description,
            explanationText,
            tone,
            simulationEvent.Sequence);
    }
}
