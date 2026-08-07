using System.ComponentModel.DataAnnotations;
using FastGateway.Dto;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FastGateway.Infrastructure;

/// <summary>
///     业务结果过滤器
/// </summary>
public sealed class ResultFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        object? result;

        try
        {
            result = await next(context);
        }
        catch (ValidationException ex)
        {
            return ResultDto.CreateFailed(ex.Message);
        }

        // 返回 void/Task 的端点由 Minimal API 产出 EmptyHttpResult；MVC 场景为 EmptyResult。
        // 二者都表示“无数据成功”，不能塞进 ResultDto.Data(object)，否则 AOT 源生成器无对应元数据会抛异常。
        if (result is EmptyHttpResult or EmptyResult) return ResultDto.CreateSuccess();

        // 端点显式返回 IResult（如文件下载的 TypedResults.PhysicalFile）时直通，
        // 由框架自己写响应体，不再包装成 ResultDto。
        //
        // 顺序要求：必须放在上面的 EmptyHttpResult 判断之后。EmptyHttpResult 自身也实现
        // IResult，两行调换会让所有返回 void 的端点退化成 204 空响应，前端拿不到
        // { success, message, data }。
        if (result is IResult httpResult) return httpResult;

        if (result is ResultDto dto) return dto;

        if (result is ResultDto<object> dtoObject) return dtoObject;

        return result == null ? ResultDto.CreateSuccess() : ResultDto.CreateSuccess(result);
    }
}