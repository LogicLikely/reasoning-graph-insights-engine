import { useState } from "react";
import "./ContactPage.css";

//part 15
function ContactForm() {

    type ContactSubmission = {
        name: string;
        email: string;
        topic: string;
        message: string;
    };

    const [name, setName] = useState("");
    const [email, setEmail] = useState("");
    const [topic, setTopic] = useState("");
    const [message, setMessage] = useState("");
    const [isSubmitting, setIsSubmitting] = useState(false);


    const submission: ContactSubmission = {
        name,
        email,
        topic,
        message,
    };

    const hasRequiredFields =
        name.trim() &&
        email.trim() &&
        topic &&
        message.trim();

    type TextFieldProps = {
        label: string;
        value: string;
        onChange: (value: string) => void;
        type?: string;
    };

    function TextField({ label, value, onChange, type = "text" }: TextFieldProps) {
        return (
            <div className="form-group">
                <label className="form-label">{label}</label>
                <input
                    className="form-input"
                    type={type}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                />
            </div>
        );
    }

    function isValidEmail(email: string) {
        return email.includes("@") && email.includes(".");
    }


    //State variable can only hold a string or null value
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);



    async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault();
        setSuccessMessage(null);
        setErrorMessage(null);

        if (!name.trim()) {
            setErrorMessage("Name is required.");
            return;
        }
        if (!isValidEmail(email)) {
            setErrorMessage("Email is required.");
            return;
        }
        if (!topic) {
            setErrorMessage("Topic is required.");
            return;
        }
        if (!message.trim()) {
            setErrorMessage("Message is required.");
            return;
        }
        const submission: ContactSubmission = {
            name,
            email,
            topic,
            message,
        };

        try {
            setIsSubmitting(true);

            await new Promise((resolve) => setTimeout(resolve, 500));

            console.log(submission);

            setSuccessMessage("Contact form submitted. Check the console for the logged data.");
            setName("");
            setEmail("");
            setTopic("");
            setMessage("");

        } finally {
            setIsSubmitting(false);
        }
    }

    return (
        <section className="contact-form-container">
            {errorMessage && (
                <div role="alert" className="form-alert error">
                    {errorMessage}
                </div>
            )}
            {successMessage && (
                <div className="form-alert success">
                    {successMessage}
                </div>
            )}

            <form onSubmit={handleSubmit} className="contact-card">
                <TextField label="Name" value={name} onChange={setName} />
                <TextField label="Email" type="email" value={email} onChange={setEmail} />

                <div className="form-group">
                    <label className="form-label">Topic</label>
                    <select
                        className="form-input"
                        value={topic}
                        onChange={(event) => setTopic(event.target.value)}
                    >
                        <option value="">Select a topic</option>
                        <option value="question">Question</option>
                        <option value="idea">Idea</option>
                        <option value="bug">Bug</option>
                        <option value="other">Other</option>
                    </select>
                </div>

                <div className="form-group">
                    <label className="form-label">Message</label>
                    <textarea
                        className="form-input form-textarea"
                        value={message}
                        onChange={(event) => setMessage(event.target.value)}
                    />
                </div>

                <button
                    type="submit"
                    className="submit-button"
                    disabled={isSubmitting || !hasRequiredFields}
                >
                    {isSubmitting ? "Submitting..." : "Submit"}
                </button>

            </form>

            <section className="preview-section">
                <h2>Preview</h2>
                <p><strong>Name:</strong> {name || "(none)"}</p>
                <p><strong>Email:</strong> {email || "(none)"}</p>
                <p><strong>Topic:</strong> {topic || "(none)"}</p>
                <p><strong>Message:</strong> {message || "(none)"}</p>
            </section>
        </section>
    );
}

export function ContactPage() {
    return (
        <div className="page-shell contact-page-shell" data-testid="contact-page">
            <section className="page-hero">
                <span className="eyebrow">Get in Touch</span>
                <h1>Contact LogicLikely</h1>
                <p>
                    Use this form to send a question, comment, or idea.
                </p>
            </section>

            <ContactForm />
        </div>
    );
}
