import { useState } from "react";

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
        setIsSubmitting(true);
        await new Promise((resolve) => setTimeout(resolve, 500));
        const submission: ContactSubmission = {
            name,
            email,
            topic,
            message,
        };
        console.log(submission);
        setSuccessMessage("Contact form submitted. Check the console for the logged data.");
        setIsSubmitting(false);
    }

    return (
        <>
            {errorMessage && (
                <p role="alert" style={{ color: "red", fontWeight: "bold" }}>
                    {errorMessage}
                </p>
            )}
            {successMessage && (
                <p style={{ color: "green" }}>
                    {successMessage}
                </p>
            )}

            <form onSubmit={handleSubmit}>
                <label>
                    Name
                    <input
                        type="text"
                        value={name}
                        onChange={(event) => setName(event.target.value)}
                    />
                </label>

                <label>
                    Email
                    <input
                        type="email"
                        value={email}
                        onChange={(event) => setEmail(event.target.value)}
                    />
                </label>

                <label>
                    Topic
                    <select
                        value={topic}
                        onChange={(event) => setTopic(event.target.value)}
                    >
                        <option value="">Select a topic</option>
                        <option value="question">Question</option>
                        <option value="idea">Idea</option>
                        <option value="bug">Bug</option>
                        <option value="other">Other</option>
                    </select>
                </label>

                <label>
                    Message
                    <textarea
                        value={message}
                        onChange={(event) => setMessage(event.target.value)}
                    />
                </label>

                <button type="submit" disabled={isSubmitting}>
                    {isSubmitting ? "Submitting..." : "Submit"}
                </button>
            </form>

            <section>
                <h2>Preview</h2>
                <p><strong>Name:</strong> {name || "(none)"}</p>
                <p><strong>Email:</strong> {email || "(none)"}</p>
                <p><strong>Topic:</strong> {topic || "(none)"}</p>
                <p><strong>Message:</strong> {message || "(none)"}</p>
            </section>
        </>
    );
}

export function ContactPage() {
    return (
        <main>
            <section>
                <h1>Contact LogicLikely</h1>
                <p>
                    Use this form to send a question, comment, or idea.
                </p>
            </section>

            <ContactForm />
        </main>
    );
}
