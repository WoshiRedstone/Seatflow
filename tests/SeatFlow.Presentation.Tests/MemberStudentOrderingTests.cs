using FluentAssertions;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 人员管理表格显示顺序：严格保持名单（导入表格）的原始先后顺序，
/// 不做任何排序（此前的姓名自然序已按需求移除）。
/// </summary>
public class MemberStudentSortingTests
{
    [Fact]
    public void 应按名单导入顺序展示_不做排序()
    {
        // 故意给出与姓名序完全相反的列表：若仍存在排序，结果会被改写。
        var students = new List<Student>
        {
            new() { Id = "z", Name = "张三" },
            new() { Id = "b", Name = "学生10" },
            new() { Id = "a", Name = "学生2" },
            new() { Id = "c", Name = "李四" },
            new() { Id = "d", Name = "阿克" },
        };

        var ordered = MemberManagementViewModel.OrderStudents(students)
            .Select(s => (s.Name, s.Id))
            .ToList();

        ordered.Should().Equal(
            ("张三", "z"),
            ("学生10", "b"),
            ("学生2", "a"),
            ("李四", "c"),
            ("阿克", "d"));
    }

    [Fact]
    public void 学生选择器_保持导入顺序_排除后仍保序()
    {
        var picker = new StudentPickerViewModel(NullDialogService.Instance);
        picker.LoadStudents(
        [
            new Student { Id = "b", Name = "学生10" },
            new Student { Id = "a", Name = "学生2" },
            new Student { Id = "c", Name = "张三" },
        ]);

        picker.Students.Select(s => s.Name).Should().Equal("学生10", "学生2", "张三");

        picker.SetExcludedIds(["a"]);
        picker.Students.Select(s => s.Name)
            .Should().Equal(["学生10", "张三"], "排除过滤不改变导入顺序");
    }
}
