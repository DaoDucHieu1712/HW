using FluentValidation;
using HW.Application.Dtos;

namespace HW.Application.Validators;

public static class BlogValidator
{
    public class CreateBlogRequestDtoValidator
        : AbstractValidator<BlogDtos.CreateBlogRequestDto>
    {
        public CreateBlogRequestDtoValidator()
        {
            RuleFor(x => x.Title)
               .NotEmpty().WithMessage("Title is required")
               .MinimumLength(5).WithMessage("Title minium length is 5 characters");

            RuleFor(x => x.Content)
                .NotEmpty().WithMessage("Content is required")
                .MaximumLength(200).WithMessage("Title maximum length is 200 characters");
        }
    }

    public class UpdateBlogRequestDtoValidator
        : AbstractValidator<BlogDtos.UpdateBlogRequestDto>
    {
        public UpdateBlogRequestDtoValidator()
        {
            RuleFor(x => x.Title)
               .NotEmpty().WithMessage("Title is required")
               .MinimumLength(5).WithMessage("Title minium length is 5 characters");

            RuleFor(x => x.Content)
                .NotEmpty().WithMessage("Content is required")
                .MaximumLength(200).WithMessage("Title maximum length is 200 characters");
        }
    }

}
