using Microsoft.AspNetCore.Mvc;
using TaskManager.WebUI.Models;

namespace TaskManager.WebUI.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected OkObjectResult OkResponse<T>(
        T data,
        string message = "ดำเนินการสำเร็จ")
    {
        return Ok(new ApiResponse<T>(
            Success: true,
            Message: message,
            Data: data));
    }

    protected ObjectResult CreatedResponse<T>(
        T data,
        string message = "สร้างข้อมูลสำเร็จ")
    {
        return StatusCode(
            StatusCodes.Status201Created,
            new ApiResponse<T>(
                Success: true,
                Message: message,
                Data: data));
    }

    protected NoContentResult NoContentResponse()
    {
        return NoContent();
    }

    protected BadRequestObjectResult BadRequestResponse(
        string message,
        object? errors = null)
    {
        return BadRequest(new ApiResponse<object?>(
            Success: false,
            Message: message,
            Errors: errors));
    }

    protected UnauthorizedObjectResult UnauthorizedResponse(
        string message = "กรุณายืนยันตัวตนก่อนใช้งาน")
    {
        return Unauthorized(new ApiResponse<object?>(
            Success: false,
            Message: message));
    }

    protected ObjectResult ForbiddenResponse(
        string message = "คุณไม่มีสิทธิ์ดำเนินการนี้")
    {
        return StatusCode(
            StatusCodes.Status403Forbidden,
            new ApiResponse<object?>(
                Success: false,
                Message: message));
    }

    protected NotFoundObjectResult NotFoundResponse(
        string message = "ไม่พบข้อมูลที่ต้องการ")
    {
        return NotFound(new ApiResponse<object?>(
            Success: false,
            Message: message));
    }

    protected ConflictObjectResult ConflictResponse(
        string message,
        object? errors = null)
    {
        return Conflict(new ApiResponse<object?>(
            Success: false,
            Message: message,
            Errors: errors));
    }

    protected ObjectResult InternalServerErrorResponse(
        string message = "ระบบเกิดข้อผิดพลาด กรุณาลองใหม่อีกครั้ง")
    {
        return StatusCode(
            StatusCodes.Status500InternalServerError,
            new ApiResponse<object?>(
                Success: false,
                Message: message));
    }
}
