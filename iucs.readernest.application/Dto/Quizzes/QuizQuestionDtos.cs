using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Quizzes
{
    public class QuizQuestionOptionInput
    {
        [Required]
        [MaxLength(200)]
        public string Text { get; set; } = null!;

        public bool IsCorrect { get; set; }
    }

    public class SaveQuizQuestionRequest
    {
        /// <summary>Required when <see cref="CourseId"/> is not set — the question is shared
        /// across every course in this department (and demo sessions in it). Ignored (derived
        /// from the course instead) when <see cref="CourseId"/> is set.</summary>
        public Guid? DepartmentId { get; set; }

        /// <summary>Optional — narrows the question to one specific course. Leave unset to
        /// share it across the whole department.</summary>
        public Guid? CourseId { get; set; }

        [Required]
        [MaxLength(500)]
        public string Prompt { get; set; } = null!;

        public int DisplayOrder { get; set; }

        [Required]
        [MinLength(2, ErrorMessage = "A question needs at least 2 options.")]
        [MaxLength(6, ErrorMessage = "A question can have at most 6 options.")]
        public List<QuizQuestionOptionInput> Options { get; set; } = [];
    }

    public class QuizQuestionOptionDto
    {
        public Guid Id { get; set; }

        public string Text { get; set; } = null!;

        // Present in every response today — the live classroom already grades quiz answers
        // entirely client-side (see ClassroomHub.AnswerQuiz's own doc comment on that trust
        // model), so withholding this from the "for a session" read wouldn't change what a
        // technically-curious student could already infer from the client bundle before this
        // feature existed. Noted here so it isn't mistaken for an oversight if server-side
        // grading is ever added later.
        public bool IsCorrect { get; set; }

        public int DisplayOrder { get; set; }
    }

    public class QuizQuestionDto
    {
        public Guid Id { get; set; }

        public Guid DepartmentId { get; set; }

        public string DepartmentName { get; set; } = null!;

        public Guid? CourseId { get; set; }

        public string? CourseName { get; set; }

        public string Prompt { get; set; } = null!;

        public int DisplayOrder { get; set; }

        public IReadOnlyList<QuizQuestionOptionDto> Options { get; set; } = [];
    }
}
